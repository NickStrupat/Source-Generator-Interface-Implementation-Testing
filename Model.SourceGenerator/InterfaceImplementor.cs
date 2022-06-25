using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

[Generator(LanguageNames.CSharp)]
class InterfaceImplementor : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		//SpinWait.SpinUntil(() => System.Diagnostics.Debugger.IsAttached, TimeSpan.FromSeconds(5));

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
		if (interfaces.IsDefaultOrEmpty)
			return;
		
		foreach (var @interface in interfaces)
		{
			var nts = GetDeclaredSymbol<INamedTypeSymbol>(@interface, out var nullableContext);
			var source = new StringBuilder(nullableContext.HasFlag(NullableContext.Enabled) ? "#nullable enable\n" : String.Empty);

#pragma warning disable RS1024
			var allPropertySymbols = nts
				.GetMembers()
				.Concat(nts.AllInterfaces.SelectMany(x => x.GetMembers()))
				.OfType<IPropertySymbol>()
				.Distinct(
					new GenericEqualityComparer<IPropertySymbol>(
						static (x, y) => x.Type.Equals(y.Type, SymbolEqualityComparer.IncludeNullability) && x.Name == y.Name, 
						static x => HashCode.Combine(x.Type, x.Name)
					)
				);
#pragma warning restore RS1024
			var propertyDetails = new List<(String name, String type, String camelName, String accessors, Boolean ctorInitRequired)>();
			foreach (var symbol in allPropertySymbols)
			{
				var property = symbol.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<PropertyDeclarationSyntax>().Single();
				var accessors = property.AccessorList!.Accessors.Select(x => x.Keyword.Kind()).ToList();
				var getterOnly = accessors.Count() == 1 && accessors.Single() == SyntaxKind.GetKeyword;
				var nonNullable = symbol.Type.NullableAnnotation == NullableAnnotation.NotAnnotated;
				var ctorInitRequired = getterOnly || nonNullable;
				propertyDetails.Add((
					symbol.Name,
					symbol.Type.ToDisplayString(),
					Char.ToLower(symbol.Name[0]) + symbol.Name[1..],
					property.AccessorList!.ToString(),
					ctorInitRequired
				));
			}
			
			Indent ind = new();
			var interfaceName = nts.ContainingNamespace.IsGlobalNamespace ? nts.ToString() : nts.ToString()[(nts.ContainingNamespace.ToString().Length + 1)..];
			const String className = "Class";
			if (!nts.ContainingNamespace.IsGlobalNamespace)
				source.AppendLine($"namespace {nts.ContainingNamespace}\n{{{++ind}");
			source.AppendLine($"{ind}public partial interface {interfaceName}\n{ind}{{");
			
			source.AppendLine($"{++ind}public sealed class {className} : {interfaceName}\n{ind}{{");
			var propertyDefinitions = propertyDetails.Select(x => $"public {x.type} {x.name} {x.accessors}");
			source.AppendLine($"{++ind}{String.Join($"\n{ind}", propertyDefinitions)}");
			var parameterDeclarations = propertyDetails.Where(x => x.ctorInitRequired).Select(x => $"{x.type} {x.camelName}");
			source.AppendLine($"\n{ind}public {className}({String.Join(", ", parameterDeclarations)}) \n{ind}{{");
			var parameterInitializations = propertyDetails.Where(x => x.ctorInitRequired).Select(x => $"{x.name} = {x.camelName};");
			source.AppendLine($"{++ind}{String.Join($"\n{ind}", parameterInitializations)}");
			source.AppendLine($"{--ind}}}\n{--ind}}}\n{--ind}}}");
			if (!nts.ContainingNamespace.IsGlobalNamespace)
				source.AppendLine($"{--ind}}}");
			
			context.AddSource(nts.ToDisplayString().Replace('<', '_').Replace('>', '_') + ".g.cs", source.ToString());
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

	struct Indent
	{
		private Int32 count;
		public override String ToString() => new String('\t', count);
		public static Indent operator ++(Indent ind) { ind.count++; return ind; }
		public static Indent operator --(Indent ind) { ind.count--; return ind; }
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