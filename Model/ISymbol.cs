namespace Model;

public interface IHasParent<T> { T Parent { get; set; } }

public partial interface ISymbol : IHasParent<ISymbol?>, IHasValue
{
	String Name { get; set; }
	String? Value { get; init; }
	String Type { get; init; }
	String Scope { get; }
	String Line { get; }
	List<String> File { get; }
}

public interface IHasValue
{
	String? Value { get; init; }
}

public partial interface IScope<T> : ISymbol
{
	public T ScopeValue { get; }
}
