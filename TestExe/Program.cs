using System.Security;
using TestLibrary;

namespace TestExe;

internal static class Program
{
    static void Main()
    {
        Console.WriteLine(typeof(AliveClassButDeadMembers));

        var a = new AliveClass();
        var b = new AliveStruct();
        var c = AliveEnum.Red;

        AliveDelegate d = () => { };

        var e = new AliveClassAndAliveMembers();
        e.AliveMethod();
        e.AliveSimpleProperty = 42;
        e.AliveComplexProperty = 42;
        e.AliveField = 42;

        try
        {

        }
        catch (MyException ex)
        {
            Console.WriteLine(ex.ToString());
        }
        
        Console.WriteLine(AliveClassAndAliveMembers.AliveConst);
        Console.WriteLine(new AliveClassAndAliveMembers.AliveNestedType());
    }
}
