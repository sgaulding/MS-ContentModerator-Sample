using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using CI.ContentModerator.Web.Helpers;
using CI.ContentModerator.Web.Models;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.CognitiveServices.ContentModerator;
using Microsoft.Azure.CognitiveServices.ContentModerator.Models;
using Microsoft.Extensions.Configuration;

namespace CI.ContentModerator.Web.Controllers
{
    public class ModerateTextController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly Regex _htmlRegex = new Regex(@"<\s*([^ >]+)[^>]*>.*?<\s*/\s*\1\s*>");

        public ModerateTextController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult ScreenText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(text));

            var contentType = GetContextType(text);

            if (contentType == "text/html")
                text = text.Replace("&nbsp;", " ");

            var screenQueue = new Queue<Screen>();
            const int maxCharacterLengthPerRequest = 1024;
            if (text.Length > maxCharacterLengthPerRequest)
            {
                var startIndex = 0;
                string textToScreen;
                while (startIndex + maxCharacterLengthPerRequest < text.Length)
                {
                    var lastIndex = text.LastIndexOf(' ', startIndex + maxCharacterLengthPerRequest);
                    textToScreen = text.Substring(startIndex, lastIndex - startIndex);

                    screenQueue.Enqueue(GetScreeningOfText(textToScreen, contentType));
                    startIndex = lastIndex;
                }

                textToScreen = text.Substring(startIndex);
                screenQueue.Enqueue(GetScreeningOfText(textToScreen, contentType));
            }
            else
            {
                screenQueue.Enqueue(GetScreeningOfText(text, contentType));
            }

            var combinedScreenings = CombineScreenings(screenQueue, text);

            return Json(combinedScreenings);
        }

        private Screen CombineScreenings(Queue<Screen> screenQueue, string originalText)
        {
            if (screenQueue == null) throw new ArgumentNullException(nameof(screenQueue));

            if (screenQueue.Count == 1) return screenQueue.First();

            var screen = screenQueue.Dequeue();
            var addToIndex = screen.OriginalText.Length;
            screen.OriginalText = originalText;


            foreach (var screenToAdd in screenQueue)
            {
                screen.NormalizedText += screenToAdd.NormalizedText;
                screen.AutoCorrectedText += screenToAdd.AutoCorrectedText;

                if (screenToAdd.Classification != null)
                {
                    screen.Classification = screen.Classification ?? new Classification();

                    screen.Classification.Category1 = new ClassificationCategory1(
                        (screen.Classification?.Category1?.Score ??
                         0 + screenToAdd.Classification?.Category1?.Score ?? 0) / 2.0);
                    screen.Classification.Category2 = new ClassificationCategory2(
                        (screen.Classification?.Category2?.Score ??
                         0 + screenToAdd.Classification?.Category2?.Score ?? 0) / 2.0);
                    screen.Classification.Category3 = new ClassificationCategory3(
                        (screen.Classification?.Category3?.Score ??
                         0 + screenToAdd.Classification?.Category3?.Score ?? 0) / 2.0);
                    screen.Classification.ReviewRecommended = screenToAdd.Classification.ReviewRecommended ?? false
                        ? true
                        : screen.Classification.ReviewRecommended;
                }

                if (string.IsNullOrWhiteSpace(screen.Status?.Exception)
                    && !string.IsNullOrWhiteSpace(screenToAdd.Status?.Exception))
                    screen.Status = screenToAdd.Status;

                if (screenToAdd.Misrepresentation != null)
                    screen.Misrepresentation = screen.Misrepresentation ?? new List<string>();

                foreach (var misrepresentation in screenToAdd.Misrepresentation ?? new List<string>())
                    screen.Misrepresentation.Add(misrepresentation);

                if (screenToAdd.Terms != null)
                    screen.Terms = screen.Terms ?? new List<DetectedTerms>();

                foreach (var detectedTerms in screenToAdd.Terms ?? new List<DetectedTerms>())
                {
                    detectedTerms.Index += addToIndex;
                    detectedTerms.OriginalIndex += addToIndex;
                    screen.Terms.Add(detectedTerms);
                }

                if (screenToAdd.PII != null)
                {
                    screen.PII = screen.PII ?? new PII();

                    if (screenToAdd.PII.Address != null)
                        screen.PII.Address = screen.PII.Address ?? new List<Address>();

                    foreach (var address in screenToAdd.PII?.Address ?? new List<Address>())
                    {
                        address.Index += addToIndex;
                        screen.PII.Address.Add(address);
                    }

                    if (screenToAdd.PII.Email != null)
                        screen.PII.Email = screen.PII.Email ?? new List<Email>();

                    foreach (var email in screenToAdd.PII?.Email ?? new List<Email>())
                    {
                        email.Index += addToIndex;
                        screen.PII.Email.Add(email);
                    }

                    if (screenToAdd.PII.IPA != null)
                        screen.PII.IPA = screen.PII.IPA ?? new List<IPA>();

                    foreach (var ipa in screenToAdd.PII?.IPA ?? new List<IPA>())
                    {
                        ipa.Index += addToIndex;
                        screen.PII.IPA.Add(ipa);
                    }

                    if (screenToAdd.PII.Phone != null)
                        screen.PII.Phone = screen.PII.Phone ?? new List<Phone>();

                    foreach (var phone in screenToAdd.PII?.Phone ?? new List<Phone>())
                    {
                        phone.Index += addToIndex;
                        screen.PII.Phone.Add(phone);
                    }

                    if (screenToAdd.PII.SSN != null)
                        screen.PII.SSN = screen.PII.SSN ?? new List<SSN>();

                    foreach (var ssn in screenToAdd.PII?.SSN ?? new List<SSN>())
                    {
                        ssn.Index += addToIndex;
                        screen.PII.SSN.Add(ssn);
                    }
                }

                addToIndex += screenToAdd.OriginalText.Length;
            }

            return screen;
        }

        private Screen GetScreeningOfText(string text, string contentType)
        {
            var azureRegion = _configuration.GetValue<string>("MsContentModerator:AzureRegion");
            var subscriptionKey = _configuration.GetValue<string>("MsContentModerator:Key");
            var maxTransactionsPerSecond = _configuration.GetValue<int>("MsContentModerator:MaxTransactionsPerSecond");

            using (var client = Clients.GetContentModeratorClient(azureRegion, subscriptionKey))
            {
                var textStream = new MemoryStream(Encoding.UTF8.GetBytes(text));
                var screen =
                    client.TextModeration.ScreenText(contentType, textStream, pII: true, classify: true);
                Thread.Sleep(TimeSpan.FromSeconds(maxTransactionsPerSecond / 60.0));


                return screen;
            }
        }

        private string GetContextType(string text)
        {
            return _htmlRegex.IsMatch(text) ? "text/html" : "text/plain";
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel {RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier});
        }
    }
}