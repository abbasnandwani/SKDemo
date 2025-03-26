using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Azure.Messaging.ServiceBus;
using SKDemoFunctionApp.DocGenerationDemo;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Server.HttpSys;
using System.Text.Json;

namespace SKDemoFunctionApp
{
    public class Function1
    {
        private readonly ILogger<Function1> _logger;

        public Function1(ILogger<Function1> logger)
        {
            _logger = logger;
        }

        [Function("Function1")]
        public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
            return new OkObjectResult("Welcome to Azure Functions!");
        }


        [Function("ProcessDoc")]
        public async Task<IActionResult> ProcessDoc([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {

            var AOAI_ENDPOINT = Environment.GetEnvironmentVariable("AOAI_ENDPOINT");
            var AOAI_DEPLOYMENT = Environment.GetEnvironmentVariable("AOAI_DEPLOYMENT");
            var AOAI_APIKEY = Environment.GetEnvironmentVariable("AOAI_APIKEY");

            // Configure the kernel with your LLM connection details
            Kernel kernel = Kernel.CreateBuilder()
                .AddAzureOpenAIChatCompletion(AOAI_DEPLOYMENT, AOAI_ENDPOINT, AOAI_APIKEY)
                .Build();

            // Create the process builder
            ProcessBuilder processBuilder = new ProcessBuilder("DocumentationGeneration");

            BuildProcess(processBuilder);

            // Build and run the process
            var process = processBuilder.Build();

            //get input
            _logger.LogInformation("Enter event name to trigger or leave blank to default: ");
            string eventStart = DocGenerationProcessEvents.Start;
            DocumentGenerationInfo docInfo = new DocumentGenerationInfo();
            docInfo.Subject = req.Query["subject"];


            var val = await process.StartAsync(kernel, new KernelProcessEvent { Id = eventStart, Data = docInfo });


            //Ask if approved
            //docInfo = DocumentGenerationInfoRepo.DocInfos[docInfo.Subject];
            docInfo = await new DocumentGenerationInfoRepo().Get(docInfo.id);

            _logger.LogInformation($"Subject: {docInfo.Subject}");
            _logger.LogInformation($"\n\nInformation: {docInfo.Information}");
            _logger.LogInformation($"\n\nGenerated Text: {docInfo.GeneratedText}");

            _logger.LogInformation("Process complete. Sent for approval.");
            return new OkObjectResult(docInfo);
        }

        [Function("Approve")]
        public async Task<IActionResult> Approve([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            var sbClient = new ServiceBusClient(Environment.GetEnvironmentVariable("SB_CONNSTR"));
            var sbSender = sbClient.CreateSender(Environment.GetEnvironmentVariable("SB_QUEUE"));

            var id = req.Query["id"];
            var yn = req.Query["yn"];

            var docInfoRepo = new DocumentGenerationInfoRepo();
            var doc = await docInfoRepo.Get(id);

            if (yn == "y")
            {
                doc.IsApproved = true;
            }
            else
            {
                doc.IsApproved = false;
            }

            docInfoRepo.AddUpdate(doc);

            //construct message
            var approvalMessage = new ApprovalMessage
            {
                Id = doc.id,
                Event = DocGenerationProcessEvents.Step3PublishDocument
            };

            ServiceBusMessage message = new ServiceBusMessage(JsonSerializer.Serialize(approvalMessage));

            message.ContentType = "application/json";

            await sbSender.SendMessageAsync(message);

            return new OkResult();
        }

        [Function("ServiceBusTrigger")]
        public async Task ServiceBusTrigger(
            [ServiceBusTrigger("%SB_QUEUE%", Connection = "SB_CONNSTR")]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions messageActions)
        {
            //_logger.LogInformation("Message ID: {id}", message.MessageId);
            //_logger.LogInformation("Message Body: {body}", message.Body);
            //_logger.LogInformation("Message Content-Type: {contentType}", message.ContentType);

            var approvalMessage = JsonSerializer.Deserialize<ApprovalMessage>(message.Body.ToString());


            #region SKProcess
            // Configure the kernel with your LLM connection details
            var AOAI_ENDPOINT = Environment.GetEnvironmentVariable("AOAI_ENDPOINT");
            var AOAI_DEPLOYMENT = Environment.GetEnvironmentVariable("AOAI_DEPLOYMENT");
            var AOAI_APIKEY = Environment.GetEnvironmentVariable("AOAI_APIKEY");

            // Configure the kernel with your LLM connection details
            Kernel kernel = Kernel.CreateBuilder()
                .AddAzureOpenAIChatCompletion(AOAI_DEPLOYMENT, AOAI_ENDPOINT, AOAI_APIKEY)
                .Build();

            // Create the process builder
            ProcessBuilder processBuilder = new ProcessBuilder("DocumentationGeneration");

            BuildProcess(processBuilder);

            // Build and run the process
            var process = processBuilder.Build();

            //get input
            _logger.LogInformation("Fetching information and executing process");

            DocumentGenerationInfoRepo repo = new DocumentGenerationInfoRepo();
            DocumentGenerationInfo docInfo = new DocumentGenerationInfo();

            docInfo = await repo.Get(approvalMessage.Id);
            string eventStart = approvalMessage.Event;


            var val = await process.StartAsync(kernel, new KernelProcessEvent { Id = eventStart, Data = docInfo });

            docInfo = await repo.Get(docInfo.id);

            _logger.LogInformation($"Subject: {docInfo.Subject}");
            _logger.LogInformation($"\n\nApproved: {docInfo.IsApproved}");
            _logger.LogInformation($"\n\nPublished: {docInfo.IsPublished}");
            #endregion

            // Complete the message
            await messageActions.CompleteMessageAsync(message);
        }

        private void BuildProcess(ProcessBuilder processBuilder)
        {
            //doc generation steps            
            var step1DocumentInfo = processBuilder.AddStepFromType<Step1DocumentInfo>();
            var step2CreateDocument = processBuilder.AddStepFromType<Step2CreateDocument>();
            var step3PublishDocument = processBuilder.AddStepFromType<Step3PublishDocument>();

            // Orchestrate the events
            processBuilder
                .OnInputEvent(DocGenerationProcessEvents.Start)
                .SendEventTo(new(step1DocumentInfo));

            processBuilder
                .OnInputEvent(DocGenerationProcessEvents.Step3PublishDocument)
                .SendEventTo(new(step3PublishDocument));

            //ends

            step1DocumentInfo
                .OnFunctionResult()
                .SendEventTo(new(step2CreateDocument));


            step2CreateDocument
                .OnEvent(DocGenerationProcessEvents.Step1DocumentInfo)
                .SendEventTo(new(step1DocumentInfo));
        }
    }


    public class ApprovalMessage
    {
        public string Id { get; set; }
        public string Event { get; set; }
    }
}
