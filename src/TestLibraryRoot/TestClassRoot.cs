using System;

namespace TestLibraryRoot
{
    public class TestClassRoot
    {
        public static int Calculate() => TestLibrary1.TestClass1.Calculate() + TestLibrary2.TestClass2.Calculate();
    }
}
