using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using TiaFds.Core;

namespace TiaFds.Openness.Xml
{
    public sealed class DataBlockDeclarationXmlParser
    {
        private static readonly Regex ArrayDeclaration = new Regex(
            @"^\s*Array\s*\[(?<bounds>.*?)\]\s+of\s+(?<type>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public DataBlockStructureInfo Parse(
            string inputPath,
            string blockName,
            int? blockNumber,
            string groupPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("An exported data-block XML path is required.", nameof(inputPath));
            if (string.IsNullOrWhiteSpace(blockName))
                throw new ArgumentException("A data-block name is required.", nameof(blockName));

            XDocument document;
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true
            };

            try
            {
                using (var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (XmlReader reader = XmlReader.Create(stream, settings))
                {
                    document = XDocument.Load(reader, LoadOptions.None);
                }
            }
            catch (XmlException exception)
            {
                throw new InvalidDataException("Exported data-block XML is malformed: " + exception.Message, exception);
            }

            var diagnostics = new List<InventoryDiagnostic>();
            var topLevel = FindTopLevelMembers(document).ToList();
            var members = new List<DataBlockMemberInfo>();
            foreach (XElement member in topLevel)
            {
                DataBlockMemberInfo parsed = ParseMember(member, blockName, 0, diagnostics);
                if (parsed != null) members.Add(parsed);
            }

            if (topLevel.Count == 0)
            {
                diagnostics.Add(new InventoryDiagnostic(
                    "Warning",
                    blockName,
                    "No declared members were found in the exported global DB XML."));
            }

            return new DataBlockStructureInfo(blockName, blockNumber, groupPath, members, diagnostics);
        }

        private static IEnumerable<XElement> FindTopLevelMembers(XDocument document)
        {
            List<XElement> sections = document
                .Descendants()
                .Where(element => element.Name.LocalName == "Section" &&
                    !element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "Member"))
                .ToList();

            if (sections.Count > 0)
            {
                return sections.SelectMany(section => section.Elements()
                    .Where(element => element.Name.LocalName == "Member"));
            }

            return document.Descendants()
                .Where(element => element.Name.LocalName == "Member" &&
                    !element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "Member"));
        }

        private static DataBlockMemberInfo ParseMember(
            XElement element,
            string parentPath,
            int nestingLevel,
            ICollection<InventoryDiagnostic> diagnostics)
        {
            string name = Attribute(element, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                diagnostics.Add(new InventoryDiagnostic(
                    "Warning",
                    parentPath,
                    "A declaration member without a Name attribute was ignored."));
                return null;
            }

            string declaredType = Attribute(element, "Datatype") ?? Attribute(element, "DataType");
            bool isArray;
            string arrayBounds;
            string dataTypeName;
            ParseDataType(declaredType, out dataTypeName, out isArray, out arrayBounds);

            string memberPath = parentPath + "." + name;
            var children = new List<DataBlockMemberInfo>();
            foreach (XElement child in element.Descendants().Where(item =>
                item.Name.LocalName == "Member" &&
                item.Ancestors().FirstOrDefault(ancestor => ancestor.Name.LocalName == "Member") == element))
            {
                DataBlockMemberInfo parsedChild = ParseMember(child, memberPath, nestingLevel + 1, diagnostics);
                if (parsedChild != null) children.Add(parsedChild);
            }

            return new DataBlockMemberInfo(
                name,
                memberPath,
                dataTypeName,
                ReadComment(element),
                nestingLevel,
                isArray,
                arrayBounds,
                children);
        }

        private static void ParseDataType(
            string declaredType,
            out string dataTypeName,
            out bool isArray,
            out string arrayBounds)
        {
            string value = declaredType == null ? string.Empty : declaredType.Trim();
            Match match = ArrayDeclaration.Match(value);
            isArray = match.Success;
            arrayBounds = match.Success ? match.Groups["bounds"].Value.Trim() : null;
            dataTypeName = Unquote(match.Success ? match.Groups["type"].Value : value);
        }

        private static string ReadComment(XElement element)
        {
            XElement text = element.Descendants()
                .FirstOrDefault(item => item.Name.LocalName == "MultiLanguageText" &&
                    item.Ancestors().FirstOrDefault(ancestor => ancestor.Name.LocalName == "Member") == element &&
                    !string.IsNullOrWhiteSpace(item.Value));
            if (text != null) return text.Value.Trim();

            XElement comment = element.Elements()
                .FirstOrDefault(item => item.Name.LocalName == "Comment" &&
                    !string.IsNullOrWhiteSpace(item.Value));
            return comment == null ? null : comment.Value.Trim();
        }

        private static string Attribute(XElement element, string localName)
        {
            XAttribute attribute = element.Attributes()
                .FirstOrDefault(item => item.Name.LocalName == localName);
            return attribute == null ? null : attribute.Value;
        }

        private static string Unquote(string value)
        {
            string result = value == null ? string.Empty : value.Trim();
            if (result.Length >= 2 && result[0] == '"' && result[result.Length - 1] == '"')
                result = result.Substring(1, result.Length - 2);
            return result.Replace("&quot;", "\"").Trim('"');
        }
    }
}
