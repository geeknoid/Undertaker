namespace TestLibrary;

public class DeadClass
{
}

public struct DeadStruct
{

}

public enum DeadEnum
{
    Red,
    Green,
    Blue,
}

public delegate void DeadDelegate();

public class AliveClassButDeadMembers
{
    private int _value;

    public class DeadNestedType
    {
    }

    public void DeadMethod()
    {
    }

    public int DeadSimpleProperty { get; set; }
    
    public int DeadComplexProperty
    {
        get { return _value / 2; }
        set { _value = value * 2;  }
    }

    public int DeadField;
    public const int DeadConst = 42;
}
