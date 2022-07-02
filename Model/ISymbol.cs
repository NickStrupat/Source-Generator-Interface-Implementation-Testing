namespace Model.Symbols;

public interface IHasParent<T> { T Parent { get; set; } }

public partial interface ISymbol : IHasParent<ISymbol?>, IHasValue, IHasValueOfOtherType
{
	String Name { get; set; }
	String? Value { get; init; }
	String Type { get; init; }
	Scope Scope { get; }
	String Line { get; }
	List<String> File { get; }
}

public interface IHasValueOfOtherType
{
	Int32 Value { get; init; }
}

public interface IHasValue
{
	String? Value { get; init; }
}

public partial interface IScope<T> : ISymbol
{
	public T ScopeValue { get; }
}


public enum Scope { Lexical, Block, Global }