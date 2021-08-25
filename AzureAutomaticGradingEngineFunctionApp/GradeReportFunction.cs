using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

using Azure;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Text.Json;
using ClosedXML.Excel;
using System.IO;


namespace AzureGraderFunctionApp
{
    public static class GradeReportFunction
    {
        [FunctionName("GradeReportFunction")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req, ILogger log, ExecutionContext context)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string assignment = req.Query["assignment"];

            CloudStorageAccount storageAccount = CloudStorage.GetCloudStorageAccount(context);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("testresult");

            var blobItems = await CloudStorage.ListBlobsFlatListing(container, assignment, log);

            var testResults = blobItems.Select(c => new { Email = ExtractEmail(c.Uri.ToString()), TestResult = GetTestResult(container, c) });

            var accumulateMarks = testResults.Aggregate(new Dictionary<string, Dictionary<string, int>>(), (acc, item) =>
             {
                 if (acc.ContainsKey(item.Email))
                 {
                     var previous = acc[item.Email];
                     var current = item.TestResult;
                     //Key is testname and Value is list of (testname,mark)
                     var result = previous.Concat(current).GroupBy(d => d.Key)
                         .ToDictionary(d => d.Key, d => d.Sum(c => c.Value));
                     acc[item.Email] = result;
                     return acc;
                 }
                 else
                 {
                     acc.Add(item.Email, item.TestResult);
                     return acc;
                 }
             });


            string jsonString = JsonSerializer.Serialize(accumulateMarks);
            log.LogInformation(jsonString);
            string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            try
            {
                using (var workbook = new XLWorkbook())
                {
                    GenerateMarksheet(accumulateMarks, workbook);
                    var stream = new MemoryStream();
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return new FileContentResult(content, contentType);
                }
            }
            catch (Exception ex)
            {
                return new OkObjectResult(ex);
            }

            //return new OkObjectResult(jsonString);
        }

        private static void GenerateMarksheet(Dictionary<string, Dictionary<string, int>> accumulateMarks, XLWorkbook workbook)
        {
            IXLWorksheet worksheet =
            workbook.Worksheets.Add("Marks");
            worksheet.Cell(1, 1).Value = "Email";
            worksheet.Cell(1, 2).Value = "Total";

            var tests = new HashSet<string>();
            for (int i = 0; i < accumulateMarks.Count(); i++)
            {
                var student = accumulateMarks.ElementAt(i);
                worksheet.Cell(i + 2, 1).Value = student.Key;
                tests.UnionWith(student.Value.Keys.ToHashSet());
                worksheet.Cell(i + 2, 2).Value = student.Value.Values.Sum();
            }
            var testList = tests.ToList();
            testList.Sort();
            for (int j = 0; j < testList.Count(); j++)
            {
                var testName = testList.ElementAt(j);
                worksheet.Cell(1, j + 3).Value = testName;
                for (int i = 0; i < accumulateMarks.Count(); i++)
                {
                    var student = accumulateMarks.ElementAt(i);
                    worksheet.Cell(i + 2, j + 3).Value = student.Value.GetValueOrDefault(testName, 0);
                }
            }
        }

       

        private static string ExtractEmail(string url)
        {
            const string MatchEmailPattern =
  @"(([\w-]+\.)+[\w-]+|([a-zA-Z]{1}|[\w-]{2,}))@"
  + @"((([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])\.([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])\."
  + @"([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])\.([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])){1}|"
  + @"([a-zA-Z]+[\w-]+\.)+[a-zA-Z]{2,4})";

            Regex rx = new Regex(
              MatchEmailPattern,
              RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Find matches.
            MatchCollection matches = rx.Matches(url);

            return matches[0].Value.ToString();

        }

        private static Dictionary<string, int> GetTestResult(CloudBlobContainer cloudBlobContainer, IListBlobItem item)
        {
            var blobName = item.Uri.ToString().Substring(cloudBlobContainer.Uri.ToString().Length + 1);
            CloudBlockBlob blob = cloudBlobContainer.GetBlockBlobReference(blobName);

            string rawXml = blob.DownloadTextAsync().Result;
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(rawXml);

            XmlNodeList testCases = xmlDoc.SelectNodes("/test-run/test-suite/test-suite/test-suite/test-case");

            var result = new Dictionary<string, int>();
            foreach (XmlNode node in testCases)
            {
                result.Add(node.Attributes["fullname"].Value, node.Attributes["result"].Value == "Passed" ? 1 : 0);
            }

            return result;
        }
    }
}