using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TiaFds.Openness.Xml;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class DataBlockXmlParserTests
    {
        [TestMethod]
        public void Parse_ReadsNamespaceNestedMembersCommentsAndArrays()
        {
            const string xml =
                "<Document xmlns=\"urn:synthetic:tia\">" +
                "<SW.Blocks.GlobalDB><AttributeList><Interface><Sections><Section Name=\"Static\">" +
                "<Member Name=\"M16006\" Datatype=\"&quot;Udt.cm.Drv&quot;\">" +
                "<Comment><MultiLanguageText Lang=\"en-US\">Drive 日本語</MultiLanguageText></Comment>" +
                "<Sections><Section Name=\"Static\"><Member Name=\"Status\" Datatype=\"Struct\">" +
                "<Member Name=\"Running\" Datatype=\"Bool\" />" +
                "</Member></Section></Sections></Member>" +
                "<Member Name=\"Drives\" Datatype=\"Array[1..20] of &quot;Udt.cm.Drv&quot;\" />" +
                "</Section></Sections></Interface></AttributeList></SW.Blocks.GlobalDB></Document>";

            DataBlockStructureInfo result = Parse(xml);

            Assert.AreEqual(2, result.Members.Count);
            Assert.AreEqual("Drives", result.Members[0].Name);
            Assert.AreEqual("Udt.cm.Drv", result.Members[0].DataTypeName);
            Assert.IsTrue(result.Members[0].IsArray);
            Assert.AreEqual("1..20", result.Members[0].ArrayBounds);
            DataBlockMemberInfo drive = result.Members[1];
            Assert.AreEqual("Drive 日本語", drive.Comment);
            Assert.AreEqual("db.cm.Drv.M16006.Status.Running",
                drive.Children[0].Children[0].MemberPath);
            Assert.AreEqual(2, drive.Children[0].Children[0].NestingLevel);
        }

        [TestMethod]
        public void Parse_ToleratesMissingOptionalAttributesAndDiagnosesNamelessMember()
        {
            const string xml =
                "<Document><Section><Member Name=\"Known\" Datatype=\"Int\" />" +
                "<Member Datatype=\"Bool\" /></Section></Document>";

            DataBlockStructureInfo result = Parse(xml);

            Assert.AreEqual(1, result.Members.Count);
            Assert.IsNull(result.Members[0].Comment);
            Assert.AreEqual(0, result.Members[0].Children.Count);
            Assert.AreEqual(1, result.Diagnostics.Count);
        }

        [TestMethod]
        public void Parse_RejectsMalformedXmlAndDtd()
        {
            Assert.ThrowsException<InvalidDataException>(() => Parse("<Document>"));
            Assert.ThrowsException<InvalidDataException>(() =>
                Parse("<!DOCTYPE Document [<!ENTITY xxe SYSTEM \"file:///c:/windows/win.ini\">]><Document>&xxe;</Document>"));
        }

        private static DataBlockStructureInfo Parse(string xml)
        {
            string directory = Path.Combine(Path.GetTempPath(), "TiaFdsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "block.xml");
            try
            {
                File.WriteAllText(path, xml, new UTF8Encoding(false));
                return new DataBlockDeclarationXmlParser().Parse(
                    path,
                    "db.cm.Drv",
                    50,
                    "Program blocks");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
