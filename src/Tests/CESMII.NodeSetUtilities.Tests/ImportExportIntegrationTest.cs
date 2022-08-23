using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.Xml;
using CESMII.OpcUa.NodeSetModel;
using CESMII.OpcUa.NodeSetModel.Factory.Opc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NodeSetDiff;
using Opc.Ua;
using Opc.Ua.Export;
using Org.XmlUnit.Diff;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: CollectionBehavior(DisableTestParallelization = true) ]

namespace CESMII.NodeSetUtilities.Tests
{
    [TestCaseOrderer("CESMII.NodeSetUtilities.Tests.ImportExportIntegrationTestCaseOrderer", "CESMII.NodeSetUtilities.Tests")]
    public class Integration
    {
        private readonly ITestOutputHelper output;

        public Integration(ITestOutputHelper output)
        {
            this.output = output;
        }

        public static bool _ImportExportPending = true;

        public const string strTestNodeSetDirectory = "TestNodeSets";

        [Theory]
        [ClassData(typeof(TestNodeSetFiles))]

        public async Task Import(string file)
        {
            file = Path.Combine(strTestNodeSetDirectory, file);
            output.WriteLine($"Importing {file}");

            var nodesetXml = File.ReadAllText(file);

            var opcContext = new DefaultOpcUaContext(NullLogger.Instance);
            var importer = new UANodeSetModelImporter(opcContext);

            var nodeSets = await importer.ImportNodeSetModelAsync(nodesetXml);
            Assert.Single(nodeSets);

            var fileName = GetFileNameFromNamespace(nodeSets[0].ModelUri);
            Assert.Equal(Path.GetFileName(file), fileName);
        }

        [Theory]
        [ClassData(typeof(TestNodeSetFiles))]
        public async Task Export(string file)
        {
            file = Path.Combine(strTestNodeSetDirectory, file);
            output.WriteLine($"Exporting {file}");

            var nodesetXml = File.ReadAllText(file);

            Dictionary<string, NodeSetModel> nodeSetModels = new();
            var opcContext = new DefaultOpcUaContext(nodeSetModels, NullLogger.Instance);
            var importer = new UANodeSetModelImporter(opcContext);

            var importedNodeSetModels = await importer.ImportNodeSetModelAsync(nodesetXml);
            Assert.Single(importedNodeSetModels);

            var importedNodeSetModel = importedNodeSetModels.FirstOrDefault();

            var exportedNodeSetXml = UANodeSetModelImporter.ExportNodeSetAsXml(importedNodeSetModel, nodeSetModels);

            // Write the exported XML
            var nodeSetFileName = GetFileNameFromNamespace(importedNodeSetModel.ModelUri);
            var nodeSetPath = Path.Combine(strTestNodeSetDirectory, "Exported", nodeSetFileName);
            if (!Directory.Exists(Path.GetDirectoryName(nodeSetPath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(nodeSetPath));
            }
            File.WriteAllText(nodeSetPath, exportedNodeSetXml);

            // Verify
            output.WriteLine($"Diffing {file}");
            {
                Diff d = OpcNodeSetXmlUnit.DiffNodeSetFiles(file, file.Replace(strTestNodeSetDirectory, Path.Combine(strTestNodeSetDirectory, "Exported")));

                OpcNodeSetXmlUnit.GenerateDiffSummary(d, out string diffControl, out string diffTest, out string diffSummary);

                var diffFileRoot = file.Replace(strTestNodeSetDirectory, Path.Combine(strTestNodeSetDirectory, "Diffs"));

                if (!Directory.Exists(Path.GetDirectoryName(diffFileRoot)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(diffFileRoot));
                }

                var summaryDiffFile = $"{diffFileRoot}.summarydiff.difflog";
                File.WriteAllText(summaryDiffFile, diffSummary);
                File.WriteAllText($"{diffFileRoot}.controldiff.difflog", diffControl);
                File.WriteAllText($"{diffFileRoot}.testdiff.difflog", diffTest);

                string expectedDiffFile = GetExpectedDiffFile(file);
                if (!File.Exists(expectedDiffFile))
                {
                    File.WriteAllText(expectedDiffFile, diffSummary);
                }

                var expectedSummary = File.ReadAllText(expectedDiffFile);

                var expectedSummaryLines = File.ReadAllLines(expectedDiffFile);
                int i = 0;
                var issueCounts = new Dictionary<string, int>();
                int unexplainedLines = 0;
                while (i < expectedSummaryLines.Length)
                {
                    var line = expectedSummaryLines[i];
                    if (line.StartsWith("###"))
                    {
                        unexplainedLines = ReportUnexplainedLines(expectedDiffFile, i, issueCounts, unexplainedLines);
                        var parts = line.Substring("###".Length).Split("#", 4);
                        int count = 1;
                        if (parts.Length > 1)
                        {
                            count = int.Parse(parts[0]);
                            bool bIsTriaged = false;
                            if (parts.Length > 2)
                            {
                                issueCounts.TryGetValue(parts[1], out var previousCount);
                                issueCounts[parts[1]] = previousCount + count;
                                if (parts[1].ToLowerInvariant() == "by design")
                                {
                                    bIsTriaged = true;
                                }
                                else
                                {
                                    if (parts.Length > 3)
                                    {
                                        var issueNumber = parts[3];
                                        if (!string.IsNullOrEmpty(issueNumber))
                                        {
                                            bIsTriaged = true;
                                        }
                                    }
                                }
                            }
                            if (!bIsTriaged)
                            {
                                output.WriteLine($"Not triaged: {expectedDiffFile}, line {i} {line}");
                                issueCounts.TryGetValue("Untriaged", out var previousCount);
                                issueCounts["Untriaged"] = previousCount + count;
                            }
                        }
                        i += count;
                    }
                    else
                    {
                        unexplainedLines++;
                    }
                    i++;
                }
                unexplainedLines = ReportUnexplainedLines(expectedDiffFile, i, issueCounts, unexplainedLines);

                var diffCounts = issueCounts.Any() ? string.Join(", ", issueCounts.Select(kv => $"{kv.Key}: {kv.Value}")) : "none";

                expectedSummary = Regex.Replace(expectedSummary, "^###.*$", "", RegexOptions.Multiline);
                // Ignore CR/LF difference in the diff files (often git induced) 
                expectedSummary = expectedSummary.Replace("\r", "").Replace("\n", "");
                diffSummary = diffSummary.Replace("\r", "").Replace("\n", "");
                Assert.True(expectedSummary == diffSummary, $"Diffs not as expected {Path.GetFullPath(summaryDiffFile)} expected {Path.GetFullPath(expectedDiffFile)}");
                output.WriteLine($"Verified export {file}. Diffs: {diffCounts}");
                if (issueCounts.TryGetValue("Untriaged", out var untriagedIssues) && untriagedIssues > 0)
                {
                    var message = $"Failed due to {untriagedIssues} untriaged issues: {diffCounts}";
                    output.WriteLine(message);
                    //Ignore for now: ideally would make as warning/yellow, but XUnit doesn't seem to allow that
                    //Assert.True(0 == untriagedIssues, message);
                }
            }

        }

        private int ReportUnexplainedLines(string expectedDiffFile, int i, Dictionary<string, int> issueCounts, int unexplainedLines)
        {
            if (unexplainedLines > 0)
            {
                var message = unexplainedLines > 1 ?
                    $"Diff lines {i - unexplainedLines + 1} to {i} have no explanation in {expectedDiffFile}."
                    : $"Diff line {i - unexplainedLines} has no explanation in {expectedDiffFile}.";
                output.WriteLine(message);
                issueCounts.TryGetValue("Untriaged", out var previousCount);
                issueCounts["Untriaged"] = previousCount + unexplainedLines;
                //Assert.True(false, message);
                unexplainedLines = 0;
            }

            return unexplainedLines;
        }

        internal static string GetExpectedDiffFile(string file)
        {
            return file.Replace(strTestNodeSetDirectory, Path.Combine(strTestNodeSetDirectory, "ExpectedDiffs")) + ".summarydiff.difflog";
        }

        public static List<ImportOPCModel> OrderImportsByDependencies(List<ImportOPCModel> importRequest)
        {
            var importsAndModels = importRequest.Select(importRequest =>
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(importRequest.Data.Replace("<Value/>", "<Value xsi:nil='true' />"))))
                {
                    var nodeSet = UANodeSet.Read(ms);
                    var modelUri = nodeSet.Models?[0].ModelUri;
                    var requiredModels = nodeSet.Models?.SelectMany(m => m.RequiredModel?.Select(rm => rm.ModelUri) ?? new List<string>())?.ToList();
                    return (importRequest, modelUri, requiredModels);
                }
            }).ToList();

            var orderedImports = new List<(ImportOPCModel, string, List<string>)>();
            var standalone = importsAndModels.Where(imr => !imr.requiredModels.Any()).ToList();
            orderedImports.AddRange(standalone);
            foreach (var imr in standalone)
            {
                importsAndModels.Remove(imr);
            }

            bool modelAdded;
            do
            {
                modelAdded = false;
                for (int i = importsAndModels.Count - 1; i >= 0; i--)
                {
                    var imr = importsAndModels[i];
                    bool bDependenciesSatisfied = true;
                    foreach (var dependency in imr.requiredModels)
                    {
                        if (!orderedImports.Any(imr => imr.Item2 == dependency))
                        {
                            bDependenciesSatisfied = false;
                            continue;
                        }
                    }
                    if (bDependenciesSatisfied)
                    {
                        orderedImports.Add(imr);
                        importsAndModels.RemoveAt(i);
                        modelAdded = true;
                    }
                }
            } while (importsAndModels.Count > 0 && modelAdded);

            //Assert.True(modelAdded, $"{importsAndModels.Count} nodesets require models not in the list.");
            orderedImports.AddRange(importsAndModels);
            var orderedImportRequest = orderedImports.Select(irm => irm.Item1).ToList();
            return orderedImportRequest;
        }
        static private string GetFileNameFromNamespace(string namespaceUri)
        {
            var fileName = namespaceUri.Replace("http://", "").Replace("/", ".");
            if (!fileName.EndsWith("."))
            {
                fileName += ".";
            }
            fileName += "NodeSet2.xml";
            return fileName;
        }

    }

    internal class TestNodeSetFiles : IEnumerable<object[]>
    {
        internal static string[] GetFiles()
        {
            var nodeSetFiles = Directory.GetFiles(Integration.strTestNodeSetDirectory);

            var importRequest = new List<ImportOPCModel>();
            foreach (var file in nodeSetFiles)
            {
                importRequest.Add(new ImportOPCModel { FileName = Path.GetFileName(file), Data = File.ReadAllText(file), });
            }
            var orderedImportRequest = Integration.OrderImportsByDependencies(importRequest);
            var orderedNodeSetFiles = orderedImportRequest.Select(r => r.FileName).ToArray();

            return orderedNodeSetFiles;
        }

        public IEnumerator<object[]> GetEnumerator()
        {
            Integration._ImportExportPending = true;
            var files = GetFiles();
            return files.Select(f => new object[] { f }).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public class ImportExportIntegrationTestCaseOrderer : ITestCaseOrderer
    {
        public ImportExportIntegrationTestCaseOrderer()
        {
        }
        public ImportExportIntegrationTestCaseOrderer(object ignored)
        {

        }
        public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases) where TTestCase : ITestCase
        {
            bool ignoreTestsWithoutExpectedOutcome = true;
            var testCasesWithExpectedDiff = testCases.ToList();
            if (ignoreTestsWithoutExpectedOutcome)
            {
                testCasesWithExpectedDiff = testCases.Where(t =>
                {
                    var file = t.TestMethodArguments[0].ToString();
                    var diffFile = Integration.GetExpectedDiffFile(Path.Combine(Integration.strTestNodeSetDirectory, file));
                    var bHasDiff = File.Exists(diffFile);
                    if (!bHasDiff)
                    {
                        Console.WriteLine($"Ignoring {file} because it has no expected diff file {diffFile}.");
                    }
                    return bHasDiff;
                }).ToList();
            }
            var importTestCaseList = testCasesWithExpectedDiff.Where(t => t.TestMethod.Method.Name == nameof(Integration.Import)).ToList();
            var testFiles = importTestCaseList.Select(t => t.TestMethodArguments[0].ToString()).ToList();
            var importRequests = testFiles.Select(file =>
            {
                var filePath = Path.Combine(Integration.strTestNodeSetDirectory, file);
                return new ImportOPCModel { FileName = filePath, Data = File.ReadAllText(filePath), };
            }).ToList();
            var orderedImportRequests = Integration.OrderImportsByDependencies(importRequests);
            // Run import tests first, in dependency order
            var orderedTestCases = orderedImportRequests.Select(ir => importTestCaseList.FirstOrDefault(tc => Path.Combine(Integration.strTestNodeSetDirectory, tc.TestMethodArguments[0].ToString()) == ir.FileName)).ToList();

            var remainingTestCaseList = testCasesWithExpectedDiff.Except(orderedTestCases).ToList();

            var remainingOrdered = orderedImportRequests.Select(ir => remainingTestCaseList.FirstOrDefault(tc => Path.Combine(Integration.strTestNodeSetDirectory, tc.TestMethodArguments[0].ToString()) == ir.FileName)).Where(tc => tc != null).ToList();
            var excludedTestCases = new List<TTestCase>();
            string[] unstableTests = new string[0];

            var unstableFileName = Path.Combine(Integration.strTestNodeSetDirectory, "ExpectedDiffs", "unstable.txt");
            if (File.Exists(unstableFileName))
            {
                File.ReadAllLines(Path.Combine(Integration.strTestNodeSetDirectory, "ExpectedDiffs", "unstable.txt"));
            }
            foreach (var remaining in remainingOrdered)
            {
                var file = remaining.TestMethodArguments[0].ToString();
                if (unstableTests.Contains(file))
                {
                    Console.WriteLine($"Not testing export for {file} because it is listed as unstable / pending investigation.");
                    excludedTestCases.Add(remaining);
                    continue;
                }
                var index = orderedTestCases.FindIndex(tc => tc.TestMethodArguments[0].ToString() == file);
                if (index >= 0)
                {
                    orderedTestCases.Insert(index + 1, remaining);
                }
                else
                {
                    orderedTestCases.Add(remaining);
                }
            }
            //orderedTestCases.AddRange(remainingOrdered);

            // Then all other tests
            var remainingUnorderedTests = testCasesWithExpectedDiff.Except(orderedTestCases).Except(excludedTestCases).ToList();

            return orderedTestCases.Concat(remainingUnorderedTests).ToList();
        }
    }
}
