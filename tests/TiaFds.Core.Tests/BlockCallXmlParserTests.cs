using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TiaFds.Openness.Xml;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class BlockCallXmlParserTests
    {
        [TestMethod]
        public void Parse_LadAndFbdCallsPreserveNetworkAndParameters()
        {
            const string xml =
                "<Document xmlns=\"urn:siemens:test\"><SW.Blocks.CompileUnit Number=\"147\">" +
                "<AttributeList><NetworkSource><FlgNet><Parts>" +
                "<Access UId=\"10\"><Symbol><Component Name=\"db.cm.Drv\"/><Component Name=\"BP_M16006\"/></Symbol></Access>" +
                "<Part Name=\"Call\" UId=\"20\"><CallInfo Name=\"cm.DrvType2\" BlockType=\"FC\">" +
                "<IntegerAttribute Name=\"Number\">52</IntegerAttribute>" +
                "<Parameter Name=\"Module\" Section=\"InOut\" Type=\"Udt.cm.Drv\"/>" +
                "<Parameter Name=\"Enable\" Section=\"Input\" Type=\"Bool\" Actual=\"#Enable\"/>" +
                "</CallInfo></Part></Parts><Wires><Wire><IdentCon UId=\"10\"/><NameCon UId=\"20\" Name=\"Module\"/></Wire></Wires>" +
                "</FlgNet></NetworkSource><MultilingualText CompositionName=\"Title\"><Text>åˆ¶å¾¡ Network</Text></MultilingualText>" +
                "</AttributeList></SW.Blocks.CompileUnit></Document>";

            BlockCallParseResult result = Parse(xml, "LAD");
            Assert.AreEqual(1, result.Calls.Count);
            BlockCallInfo call = result.Calls[0];
            Assert.AreEqual("cm.DrvType2", call.CalledBlockName);
            Assert.AreEqual(52, call.CalledBlockNumber);
            Assert.AreEqual(147, call.NetworkNumber);
            Assert.AreEqual("åˆ¶å¾¡ Network", call.NetworkTitle);
            Assert.AreEqual(2, call.Parameters.Count);
            CallParameterInfo module = System.Linq.Enumerable.Single(
                call.Parameters, item => item.FormalName == "Module");
            Assert.AreEqual("\"db.cm.Drv\".BP_M16006", module.ActualExpression);
            Assert.AreEqual("db.cm.Drv.BP_M16006", module.ResolvedMemberPath);
        }

        [TestMethod]
        public void Parse_MultipleCallsInOneNetworkAndMissingNumberAreDeterministic()
        {
            const string xml =
                "<Document><Network><Part UId=\"1\"><CallInfo Name=\"cm.DrvType0\" BlockType=\"FC\"><Parameter Name=\"Module\" Section=\"InOut\" Type=\"Udt.cm.Drv\" Actual=\"db.cm.Drv.A\"/></CallInfo></Part>" +
                "<Part UId=\"2\"><CallInfo Name=\"cm.DrvType1\" BlockType=\"FC\"><Parameter Name=\"Module\" Section=\"InOut\" Type=\"Udt.cm.Drv\" Actual=\"db.cm.Drv.B[2]\"/></CallInfo></Part></Network></Document>";
            BlockCallParseResult result = Parse(xml, "FBD");
            Assert.AreEqual(2, result.Calls.Count);
            Assert.AreEqual(1, result.Calls[0].CallOrdinal);
            Assert.AreEqual(2, result.Calls[1].CallOrdinal);
            Assert.IsNull(result.Calls[0].NetworkNumber);
            Assert.AreEqual("db.cm.Drv.B[2]", result.Calls[1].Parameters[0].ResolvedMemberPath);
        }

        [TestMethod]
        public void Parse_UnsupportedLanguageProducesDiagnosticAndMalformedOrDtdIsRejected()
        {
            BlockCallParseResult unsupported = Parse("<Document />", "SCL");
            Assert.AreEqual("CM110_UNSUPPORTED_BLOCK_LANGUAGE", unsupported.Diagnostics[0].Code);
            Assert.ThrowsException<InvalidDataException>(() => Parse("<Document>", "LAD"));
            Assert.ThrowsException<InvalidDataException>(() => Parse(
                "<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><Document>&e;</Document>", "LAD"));
        }

        [DataTestMethod]
        [DataRow("LAD")]
        [DataRow("FBD")]
        public void Parse_ResolvesV151FormalPortUidThroughWireToQuotedDbAccess(string language)
        {
            const string xml =
                "<Document xmlns=\"urn:siemens:v15.1\"><SW.Blocks.CompileUnit Number=\"1\">" +
                "<Part Name=\"Call\" UId=\"20\"><CallInfo Name=\"cm.DrvType1\" BlockType=\"FC\">" +
                "<IntegerAttribute Name=\"Number\">51</IntegerAttribute>" +
                "<Parameter UId=\"21\" Name=\"Drv\" Section=\"InOut\" Type=\"Udt.cm.Drv\"/>" +
                "</CallInfo></Part>" +
                "<Access Scope=\"GlobalVariable\" UId=\"10\"><Symbol>" +
                "<Component Name=\"&quot;db.cm.Drv&quot;\"/><Component Name=\"BP_M16001\"/>" +
                "</Symbol></Access>" +
                "<Wire UId=\"30\"><IdentCon UId=\"10\"/><IdentCon UId=\"21\"/></Wire>" +
                "<MultilingualText CompositionName=\"Title\"><Text>BP_M16001 Network</Text></MultilingualText>" +
                "</SW.Blocks.CompileUnit></Document>";

            BlockCallParseResult result = Parse(xml, language,
                new System.Collections.Generic.HashSet<string>(
                    new[] { "db.cm.Drv.BP_M16001" }, StringComparer.OrdinalIgnoreCase));
            CallParameterInfo parameter = result.Calls[0].Parameters[0];
            Assert.AreEqual("\"db.cm.Drv\".BP_M16001", parameter.ActualExpression);
            Assert.AreEqual("db.cm.Drv.BP_M16001", parameter.ResolvedMemberPath);
            Assert.AreEqual(0, result.Calls[0].Diagnostics.Count);
        }

        [TestMethod]
        public void Parse_RendersInputOutputLocalArrayConstantAndAbsoluteOperands()
        {
            const string xml =
                "<Document><Network Number=\"2\"><Part UId=\"20\"><CallInfo Name=\"Generic\" BlockType=\"FC\">" +
                "<Parameter UId=\"21\" Name=\"Nested\" Section=\"Input\" Type=\"Int\"/>" +
                "<Parameter UId=\"22\" Name=\"Local\" Section=\"Output\" Type=\"Int\"/>" +
                "<Parameter UId=\"23\" Name=\"Constant\" Section=\"Input\" Type=\"Bool\"/>" +
                "<Parameter UId=\"24\" Name=\"Absolute\" Section=\"Input\" Type=\"Bool\"/>" +
                "<Parameter UId=\"25\" Name=\"Optional\" Section=\"Output\" Type=\"Bool\"/>" +
                "</CallInfo></Part>" +
                "<Access UId=\"10\" Scope=\"GlobalVariable\"><Symbol><Component Name=\"&quot;db.cm.Drv&quot;\"/>" +
                "<Component Name=\"Drives\"><Access Scope=\"LiteralConstant\"><Constant><ConstantValue>3</ConstantValue></Constant></Access></Component>" +
                "<Component Name=\"Status\"/></Symbol></Access>" +
                "<Access UId=\"11\" Scope=\"LocalVariable\"><Symbol><Component Name=\"LocalDrive\"/></Symbol></Access>" +
                "<Access UId=\"12\" Scope=\"LiteralConstant\"><Constant><ConstantValue>TRUE</ConstantValue></Constant></Access>" +
                "<Access UId=\"13\" Scope=\"Absolute\"><Address Area=\"DB\" Type=\"X\" BlockNumber=\"50\" ByteOffset=\"0\" BitOffset=\"0\"/></Access>" +
                "<Wire><IdentCon UId=\"10\"/><IdentCon UId=\"21\"/></Wire>" +
                "<Wire><IdentCon UId=\"22\"/><IdentCon UId=\"11\"/></Wire>" +
                "<Wire><IdentCon UId=\"12\"/><IdentCon UId=\"23\"/></Wire>" +
                "<Wire><IdentCon UId=\"13\"/><IdentCon UId=\"24\"/></Wire>" +
                "</Network></Document>";
            BlockCallInfo call = Parse(xml, "FBD").Calls[0];
            Assert.AreEqual("\"db.cm.Drv\".Drives[3].Status", Parameter(call, "Nested").ActualExpression);
            Assert.AreEqual("#LocalDrive", Parameter(call, "Local").ActualExpression);
            Assert.AreEqual("TRUE", Parameter(call, "Constant").ActualExpression);
            Assert.AreEqual("DB50.DBX0.0", Parameter(call, "Absolute").ActualExpression);
            Assert.IsNull(Parameter(call, "Optional").ActualExpression);
            Assert.IsFalse(call.Diagnostics.Any(item => item.Code == "CM116_PARAMETER_CONNECTION_NOT_FOUND"));
        }

        [TestMethod]
        public void Parse_DiagnosesAmbiguousBrokenAndUnsupportedConnectionsWithoutGuessing()
        {
            const string xml =
                "<Document><Network><Part UId=\"20\"><CallInfo Name=\"Generic\" BlockType=\"FC\">" +
                "<Parameter UId=\"21\" Name=\"Ambiguous\" Section=\"InOut\" Type=\"Int\"/>" +
                "<Parameter UId=\"22\" Name=\"Broken\" Section=\"Input\" Type=\"Int\"/>" +
                "<Parameter UId=\"23\" Name=\"Unsupported\" Section=\"Input\" Type=\"Int\"/>" +
                "</CallInfo></Part>" +
                "<Access UId=\"10\" Scope=\"GlobalVariable\"><Symbol><Component Name=\"A\"/></Symbol></Access>" +
                "<Access UId=\"11\" Scope=\"GlobalVariable\"><Symbol><Component Name=\"B\"/></Symbol></Access>" +
                "<Access UId=\"12\" Scope=\"Unknown\"/>" +
                "<Wire><IdentCon UId=\"10\"/><IdentCon UId=\"11\"/><IdentCon UId=\"21\"/></Wire>" +
                "<Wire><IdentCon UId=\"999\"/><IdentCon UId=\"22\"/></Wire>" +
                "<Wire><IdentCon UId=\"12\"/><IdentCon UId=\"23\"/></Wire>" +
                "</Network></Document>";
            BlockCallInfo call = Parse(xml, "LAD").Calls[0];
            Assert.IsTrue(call.Diagnostics.Any(item => item.Code == "CM117_PARAMETER_CONNECTION_AMBIGUOUS"));
            Assert.IsTrue(call.Diagnostics.Any(item => item.Code == "CM120_CONNECTION_REFERENCE_NOT_FOUND"));
            Assert.IsTrue(call.Diagnostics.Any(item => item.Code == "CM118_CONNECTED_OPERAND_NOT_SUPPORTED"));
            Assert.IsTrue(call.Parameters.All(item => item.ActualExpression == null));
        }

        private static BlockCallParseResult Parse(string xml, string language)
        {
            return Parse(xml, language, null);
        }

        private static BlockCallParseResult Parse(
            string xml,
            string language,
            System.Collections.Generic.ISet<string> knownPaths)
        {
            string directory = Path.Combine(Path.GetTempPath(), "TiaFdsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "block.xml");
            try
            {
                File.WriteAllText(path, xml, new UTF8Encoding(false));
                return new BlockCallXmlParser().Parse(path, "Main", 100, "Function",
                    "Program blocks/Main", language, knownPaths);
            }
            finally { Directory.Delete(directory, true); }
        }

        private static CallParameterInfo Parameter(BlockCallInfo call, string name)
        {
            return call.Parameters.Single(item => item.FormalName == name);
        }
    }
}
