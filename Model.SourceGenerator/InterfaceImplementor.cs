using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Model.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class InterfaceImplementor : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		//SpinWait.SpinUntil(() => System.Diagnostics.Debugger.IsAttached, TimeSpan.FromSeconds(20));

		var partialAndNonGenericInterfaceDeclarations = context.SyntaxProvider
			.CreateSyntaxProvider(
				predicate: static (s, _) => s is InterfaceDeclarationSyntax /*{TypeParameterList: null}*/ ids && ids.Modifiers.Count(x => x.IsKind(SyntaxKind.PartialKeyword)) == 1,
				transform: static (ctx, _) => (InterfaceDeclarationSyntax) ctx.Node);

		var compilationAndPartialInterfaces = context.CompilationProvider.Combine(partialAndNonGenericInterfaceDeclarations.Collect());

		// Generate the source using the compilation and enums
		context.RegisterSourceOutput(compilationAndPartialInterfaces, static (spc, source) => Execute(source.Left, source.Right, spc));
	}

	static void Execute(Compilation compilation, ImmutableArray<InterfaceDeclarationSyntax> interfaces, SourceProductionContext context)
	{
		foreach (var @interface in interfaces)
			ProcessInterface(@interface);

		void ProcessInterface(InterfaceDeclarationSyntax interfaceDeclarationSyntax)
		{
			var symbol = GetDeclaredSymbol<INamedTypeSymbol>(interfaceDeclarationSyntax, out var nullableContext);
			var source = new StringBuilder(nullableContext.HasFlag(NullableContext.Enabled) ? "#nullable enable\n" : String.Empty);

			// var declaredProperties = symbol.GetMembers().OfType<IPropertySymbol>();
			// var inheritedProperties = symbol.AllInterfaces.SelectMany(x => x.GetMembers()).OfType<IPropertySymbol>();
			var allPropertySymbols = symbol
				.GetMembers()
				.Concat(symbol.AllInterfaces.SelectMany(x => x.GetMembers()))
				.OfType<IPropertySymbol>()
				.Distinct(
					new GenericEqualityComparer<IPropertySymbol>(
						static (x, y) => x.Type.Equals(y.Type, SymbolEqualityComparer.IncludeNullability) && x.Name == y.Name,
#pragma warning disable RS1024
						static x => HashCode.Combine(x.Type, x.Name)
#pragma warning restore RS1024
					)
				);
			var propertyDetails =
				from propertySymbol in allPropertySymbols
				let property = propertySymbol.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<PropertyDeclarationSyntax>().Single()
				where property.AccessorList is not null
				let accessors = property.AccessorList.Accessors.Select(x => x.Keyword.Kind()).ToList()
				select (
					name: propertySymbol.Name,
					type: propertySymbol.Type.ToDisplayString(),
					camelCase: Char.ToLower(propertySymbol.Name[0]) + propertySymbol.Name[1..],
					get: accessors.Contains(SyntaxKind.GetKeyword),
					set: accessors.Contains(SyntaxKind.SetKeyword),
					init: accessors.Contains(SyntaxKind.InitKeyword),
					refType: propertySymbol.Type.IsReferenceType,
					nonNullable: propertySymbol.Type.NullableAnnotation == NullableAnnotation.NotAnnotated,
					isCollection: propertySymbol.Type.AllInterfaces.Any(x => x.ConstructedFrom.ToString() == "System.Collections.Generic.ICollection<T>"),
					hasParameterlessConstructor: propertySymbol.Type is {IsValueType: true} ||
					                             propertySymbol.Type is INamedTypeSymbol nts && nts.Constructors.Any(x => x.Parameters.IsEmpty)
				);

			Indent ind = new();
			var interfaceName = symbol.ContainingNamespace.IsGlobalNamespace ? symbol.ToString() : symbol.ToString()[(symbol.ContainingNamespace.ToString().Length + 1)..];
			const String className = "Class";
			if (!symbol.ContainingNamespace.IsGlobalNamespace)
				source.AppendLine($"namespace {symbol.ContainingNamespace};\n");
			source.AppendLine($"{ind}public partial interface {interfaceName}\n{ind}{{");

			source.AppendLine($"{++ind}public sealed class {className} : {interfaceName}\n{ind}{{");
			var propertyDefinitions = propertyDetails.Select(x =>
			{
				var initList = x.isCollection && x.hasParameterlessConstructor;
				var needsInit = x.init || !initList;
				var setter = x.set ? "set; " : needsInit ? "init; " : String.Empty;
				var needsDefaultValue = !x.init && initList;
				var required = x.refType && x.nonNullable && !needsDefaultValue ? "required " : null;
				var defaultValue = needsDefaultValue ? " = new();" : String.Empty;
				return $"public {required}{x.type} {x.name} {{ get; {setter}}}{defaultValue}";
			});
			source.AppendLine($"{++ind}{String.Join($"\n{ind}", propertyDefinitions)}");

			source.AppendLine($"{--ind}}}");
			if (!symbol.ContainingNamespace.IsGlobalNamespace)
				source.AppendLine($"{--ind}}}");

			context.AddSource(symbol.ToDisplayString().Replace('<', '_').Replace('>', '_') + ".g.cs", source.ToString());
		}

		TSymbol GetDeclaredSymbol<TSymbol>(SyntaxNode node, out NullableContext nc) where TSymbol : ISymbol
		{
			var model = compilation.GetSemanticModel(node.SyntaxTree);
			nc = model.GetNullableContext(node.SpanStart);
			var symbol = model.GetDeclaredSymbol(node);
			return symbol switch
			{
				TSymbol x => x,
				null => throw new Exception($"{node.Kind()} has no declared symbol"),
				_ => throw new InvalidOperationException($"Expected `{typeof(TSymbol).Name}` but got `{symbol.GetType().Name}`")
			};
		}
	}

	class Indent
	{
		private Int32 count;
		public override String ToString() => new('\t', count);
		public static Indent operator ++(Indent ind) { ind.count++; return ind; }
		public static Indent operator --(Indent ind) { ind.count--; return ind; }
		public static implicit operator String(Indent ind) => ind.ToString();
	}

	sealed class GenericEqualityComparer<T> : IEqualityComparer<T> where T : class
	{
		private readonly Func<T, T, Boolean> equals;
		private readonly Func<T, Int32> getHashCode;
		public GenericEqualityComparer(Func<T, T, Boolean> equals, Func<T, Int32> getHashCode) => (this.equals, this.getHashCode) = (equals, getHashCode);
		public Boolean Equals(T? x, T? y)
		{
			if (ReferenceEquals(x, y))
				return true;
			if (ReferenceEquals(x, null) || ReferenceEquals(y, null))
				return false;
			return equals(x, y);
		}

		public Int32 GetHashCode(T obj) => getHashCode(obj);
	}
}
