namespace TestLibrary;

public class AliveClass
{
}

public struct AliveStruct
{

}

public enum AliveEnum
{
    Red,
    Green,
    Blue,
}

public delegate void AliveDelegate();

public class AliveClassAndAliveMembers
{
    private int _value;

    public class AliveNestedType
    {
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>")]
    public void AliveMethod()
    {
        _ = new List<GenericTypeArgument>();
    }

    public int AliveSimpleProperty { get; set; }

    public int AliveComplexProperty
    {
        get { return _value / 2; }
        set { _value = value * 2; }
    }

    public event EventHandler AliveEvent
    {
        add { }
        remove { }
    }

    public int this[int index]
    {
        get { return 0; }
        set { _ = value; }
    }

    public int AliveField;
    public const int AliveConst = 42;
}

public class MyException : Exception
{
}

public class GenericTypeArgument
{
}
