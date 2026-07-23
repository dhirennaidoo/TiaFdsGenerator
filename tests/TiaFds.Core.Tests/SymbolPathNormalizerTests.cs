using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class SymbolPathNormalizerTests
    {
        [DataTestMethod]
        [DataRow("\"db.cm.Drv\".BP_M16006", "db.cm.Drv.BP_M16006")]
        [DataRow("db.cm.Drv.BP_M16006", "db.cm.Drv.BP_M16006")]
        [DataRow("  \"db.cm.Drv\" . BP_M16006  ", "db.cm.Drv.BP_M16006")]
        [DataRow("\"db\"\"name\".Member", "db\"name.Member")]
        [DataRow("db.cm.Drv.Drives[2]", "db.cm.Drv.Drives[2]")]
        [DataRow("db.cm.Drv.Group.Member", "db.cm.Drv.Group.Member")]
        public void Normalize_SymbolicMemberPaths(string expression, string expected)
        {
            SymbolPathNormalizationResult result = new PlcSymbolPathNormalizer().Normalize(expression);
            Assert.IsTrue(result.IsSymbolicMemberPath);
            Assert.AreEqual(expected, result.NormalizedPath);
            Assert.AreEqual(expression, result.OriginalExpression);
        }

        [DataTestMethod]
        [DataRow("DB50.DBX0.0")]
        [DataRow("#LocalDrive")]
        [DataRow("P##Drive")]
        [DataRow("")]
        [DataRow("\"unterminated.Member")]
        [DataRow("SingleName")]
        public void Normalize_DoesNotClaimResolutionForUnsupportedExpressions(string expression)
        {
            SymbolPathNormalizationResult result = new PlcSymbolPathNormalizer().Normalize(expression);
            Assert.IsFalse(result.IsSymbolicMemberPath);
            Assert.IsNull(result.NormalizedPath);
        }
    }
}
