using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestLibrary;

[TestClass]
public class AliveByTestClas
{
    [TestMethod]
    public void MyTestMethod()
    {
        var x = new OnlyUsedInATest();
        Console.WriteLine(x);
    }

    private class OnlyUsedInATest
    {
    }
}