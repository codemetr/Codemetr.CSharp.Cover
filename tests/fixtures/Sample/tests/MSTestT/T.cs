using Microsoft.VisualStudio.TestTools.UnitTesting;
[TestClass] public class T { [TestMethod] public void Items() => Assert.AreEqual(2, new Shop.Calc().Items(2).Count()); }
