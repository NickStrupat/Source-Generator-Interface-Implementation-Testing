using Model.Symbols;

ISymbol a = new ISymbol.Class("name", "type", "scope", "line", new() { "file1", "file2" }) { Value = "value" };
//IScope b = new IScope.Class("name", "type", "scope", "line", new() { "file1", "file2" }) { Value = "value" };
;
IFoo foo = new IFoo.Class("name");
IWhat what = new IWhat.Class("who", "okay");
;
public partial interface IFoo
{
    String Name { get; }
}

partial interface IWhat
{
    String Who { get; }
    String Okay { get; init; }
}