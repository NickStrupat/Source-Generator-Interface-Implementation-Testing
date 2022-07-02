using System.Collections;
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
			var symbol = GetDeclaredSymbol<INamedTypeSymbol>(@interface, out var nullableContext);
			var source = new StringBuilder(nullableContext.HasFlag(NullableContext.Enabled) ? "#nullable enable\n" : String.Empty);

#pragma warning disable RS1024
			var allPropertySymbols = symbol
				.GetMembers()
				.Concat(symbol.AllInterfaces.SelectMany(x => x.GetMembers()))
				.OfType<IPropertySymbol>()
				.Distinct(
					new GenericEqualityComparer<IPropertySymbol>(
						static (x, y) => x.Type.Equals(y.Type, SymbolEqualityComparer.IncludeNullability) && x.Name == y.Name, 
						static x => HashCode.Combine(x.Type, x.Name)
					)
				);
#pragma warning restore RS1024
			var propertyDetails = new List<(String name, String type, String camelName, String accessors, Boolean ctorInitRequired, Boolean isConstructableWithoutArguments)>();
			foreach (var propertySymbol in allPropertySymbols)
			{
				var property = propertySymbol.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<PropertyDeclarationSyntax>().Single();
				if (property.AccessorList is not null)
					continue;
				var accessors = property.AccessorList!.Accessors.Select(x => x.Keyword.Kind()).ToList();
				var getterOnly = accessors.Count() == 1 && accessors.Single() == SyntaxKind.GetKeyword;
				var nonNullable = propertySymbol.Type.NullableAnnotation == NullableAnnotation.NotAnnotated;
				var ctorInitRequired = getterOnly || nonNullable;
				var isCollection = propertySymbol.Type.AllInterfaces.Any(x => x.ToString() == typeof(ICollection).FullName);
				var hasParameterlessConstructor =
					propertySymbol.Type is {IsValueType: true} ||
					propertySymbol.Type is INamedTypeSymbol nts && nts.Constructors.Any(x => x.Parameters.IsEmpty);
				var isConstructableWithoutArguments = isCollection && hasParameterlessConstructor;
				propertyDetails.Add((
					propertySymbol.Name,
					propertySymbol.Type.ToDisplayString(),
					Char.ToLower(propertySymbol.Name[0]) + propertySymbol.Name[1..],
					property.AccessorList!.ToString(),
					ctorInitRequired,
					isConstructableWithoutArguments
				));
			}
			
			Indent ind = new();
			var interfaceName = symbol.ContainingNamespace.IsGlobalNamespace ? symbol.ToString() : symbol.ToString()[(symbol.ContainingNamespace.ToString().Length + 1)..];
			const String className = "Class";
			if (!symbol.ContainingNamespace.IsGlobalNamespace)
				source.AppendLine($"namespace {symbol.ContainingNamespace}\n{{{++ind}");
			source.AppendLine($"{ind}public partial interface {interfaceName}\n{ind}{{");
			
			source.AppendLine($"{++ind}public sealed class {className} : {interfaceName}\n{ind}{{");
			var propertyDefinitions = propertyDetails.Select(x => $"public {x.type} {x.name} {x.accessors}");
			source.AppendLine($"{++ind}{String.Join($"\n{ind}", propertyDefinitions)}");
			
			// ctor with all properties that require assignment
			var parameterDeclarations = propertyDetails.Where(x => x.ctorInitRequired).Select(x => $"{x.type} {x.camelName}");
			source.AppendLine($"\n{ind}public {className}({String.Join(", ", parameterDeclarations)})\n{ind}{{");
			var parameterInitializations = propertyDetails.Where(x => x.ctorInitRequired).Select(x => $"{x.name} = {x.camelName};");
			source.AppendLine($"{++ind}{String.Join($"\n{ind}", parameterInitializations)}");
			source.AppendLine($"{--ind}}}");
			
			// ctor with all properties that require assignment, less the ones that implement ICollection and have a parameterless constructor
			var parameterDeclarations2 = propertyDetails.Where(x => x.ctorInitRequired && !x.isConstructableWithoutArguments).Select(x => $"{x.type} {x.camelName}");
			if (parameterDeclarations.Count() != parameterDeclarations2.Count())
			{
				source.AppendLine($"\n{ind}public {className}({String.Join(", ", parameterDeclarations2)})\n{ind}{{");
				++ind;
				foreach (var (name, type, camelName, _, _, isConstructableWithoutArguments) in propertyDetails.Where(x => x.ctorInitRequired))
					source.AppendLine(isConstructableWithoutArguments ? $"{ind}{name} = new {type}();" : $"{ind}{name} = {camelName};");
				source.AppendLine($"{--ind}}}");
			}
			
			source.AppendLine($"{--ind}}}\n{--ind}}}");
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