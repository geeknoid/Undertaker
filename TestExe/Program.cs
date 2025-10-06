using TestLibrary;

namespace TestExe;

internal static class Program
{
    static void Main()
    {
        Console.WriteLine(typeof(AliveClassButDeadMembers));

        var a = new AliveClass();
#pragma warning disable CS0219 // Variable is assigned but its value is never used
        var b = new AliveStruct();
        var c = AliveEnum.Red;
#pragma warning restore CS0219 // Variable is assigned but its value is never used

#pragma warning disable IDE0039 // Use local function
        AliveDelegate d = () => { };
#pragma warning restore IDE0039 // Use local function

        var r = new AliveRecordClass(1, "Test");

        var e = new AliveClassAndAliveMembers();
        e.AliveMethod();
        e.AliveSimpleProperty = 42;
        e.AliveComplexProperty = 42;
        e.AliveField = 42;
        e.AliveEvent += (sender, args) => { };
        e[0] = 42;

        try
        {

        }
        catch (MyCaughtException ex)
        {
            Console.WriteLine(ex.ToString());
        }

        IAliveInterface f = a;
        f.Func();

        Console.WriteLine(AliveClassAndAliveMembers.AliveConst);
        Console.WriteLine(new AliveClassAndAliveMembers.AliveNestedType());

        throw new MyThrownException();
    }
}
