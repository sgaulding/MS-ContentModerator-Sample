using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using CI.ContentModerator.Web.Helpers;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.CognitiveServices.ContentModerator;
using Microsoft.Azure.CognitiveServices.ContentModerator.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Rest;

namespace CI.ContentModerator.Web.Controllers
{
    public class ModerateImageController : Controller
    {
        private readonly IConfiguration _configuration;



        public ModerateImageController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public IActionResult EvaluateFileInput(List<IFormFile> files)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));

            var size = files.Sum(f => f.Length);

            // full path to file in temp location

            var imageEvaluationInfos = new List<ImageEvaluationInfo>();

            foreach (var formFile in files)
            {
                if (formFile.Length <= 0) continue;

                var azureRegion = _configuration.GetValue<string>("MsContentModerator:AzureRegion");
                var subscriptionKey = _configuration.GetValue<string>("MsContentModerator:Key");
                var maxTransactionsPerSecond =
                    _configuration.GetValue<int>("MsContentModerator:MaxTransactionsPerSecond");

                using (var client = Clients.GetContentModeratorClient(azureRegion, subscriptionKey))
                {
                    var evaluate = client.ImageModeration.EvaluateFileInput(formFile.OpenReadStream());
                    Thread.Sleep(TimeSpan.FromSeconds(maxTransactionsPerSecond / 60.0));
                    var ocr = client.ImageModeration.OCRFileInput("eng", formFile.OpenReadStream());
                    Thread.Sleep(TimeSpan.FromSeconds(maxTransactionsPerSecond / 60.0));
                    var foundFaces = client.ImageModeration.FindFacesFileInput(formFile.OpenReadStream());
                    Thread.Sleep(TimeSpan.FromSeconds(maxTransactionsPerSecond / 60.0));

                    imageEvaluationInfos.Add(new ImageEvaluationInfo
                    {
                        ImageName = formFile.FileName,
                        Evaluate = evaluate,
                        Ocr = ocr,
                        FoundFaces = foundFaces
                    });
                }
            }

            return View("Index", imageEvaluationInfos);

        }
    }

    public class ImageEvaluationInfo
    {
        public string ImageName { get; set; }
        public Evaluate Evaluate { get; set; }
        public OCR Ocr { get; set; }
        public FoundFaces FoundFaces { get; set; }
    }
}


