using Model.Symbols;

ISymbol a = new ISymbol.Class { Name = "name", Type = "type", Scope = Scope.Lexical, Line = "line", File = { "file1", "file2" }, OtherFile = new(), Value = "value" };
//IScope b = new IScope.Class("name", "type", "scope", "line", new() { "file1", "file2" }) { Value = "value" };
;
// IFoo foo = new IFoo.Class("name");
// IWhat what = new IWhat.Class("who", "okay");
// ;
// public partial interface IFoo
// {
//     String Name { get; }
// }
//
public interface IWhat : IBar
{
    String Who { get; }
    String Okay { get; init; }

    public sealed class Class : IWhat
    {
        private readonly Int32 woo;
        private String woo1;
        public required String Who { get; init; }
        public required String Okay { get; init; }

        Int32 IBaz.Woo
        {
            get => woo;
            init => woo = value;
        }

        String IBar.Woo
        {
            get => woo1;
            init => woo1 = value;
        }
    }
}

public interface IBar : IBaz
{
    new String Woo { get; init; }
}

public interface IBaz
{
    Int32 Woo { get; init; }
}
