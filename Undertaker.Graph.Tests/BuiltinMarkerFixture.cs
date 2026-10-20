namespace Undertaker.Graph.Tests
{
    public sealed class BuiltinMarkerFixture
    {
        private int _value;

        [Microsoft.VisualStudio.TestTools.UnitTesting.STATestMethod]
        public void StaTestMethod() => _value++;

        [Microsoft.AspNetCore.Mvc.AcceptVerbs]
        public void ReflectionMethod() => _value++;
    }
}

namespace Microsoft.AspNetCore.Mvc
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class AcceptVerbsAttribute : Attribute;
}
