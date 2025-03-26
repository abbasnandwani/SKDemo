using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel;
using ProcessAutomation.DocGenerationProcess;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.ComponentModel;
using System.Text.Json;


namespace ProcessAutomation
{
    internal class Program
    {
        static async Task Main(string[] args)
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
            Console.Write("Enter event name to trigger or leave blank to default: ");
            string? eventStart = Console.ReadLine();
            DocumentGenerationInfo docInfo = new DocumentGenerationInfo();

            if (string.IsNullOrEmpty(eventStart))
            {
                //eventStart = DemoProcessEvents.Start;
                eventStart = DocGenerationProcessEvents.Start;
                docInfo.Subject = "Contoso GlowBrew";
            }
            //***

            var val = await process.StartAsync(kernel, new KernelProcessEvent { Id = eventStart, Data = docInfo });


            //Ask if approved
            //docInfo = DocumentGenerationInfoRepo.DocInfos[docInfo.Subject];
            docInfo = await new DocumentGenerationInfoRepo().Get(docInfo.id);

            Console.WriteLine($"Subject: {docInfo.Subject}");
            Console.WriteLine($"\n\nInformation: {docInfo.Information}");
            Console.WriteLine($"\n\nGenerated Text: {docInfo.GeneratedText}");

            Console.Write("Do you approve the documentation? (y/n): ");
            var input = Console.ReadLine();

            if(input == "y")
            {
                docInfo.IsApproved = true;                
            }
            else
            {
                docInfo.IsApproved = false;
            }

            // Restart the process with approval for publishing the documentation.
            await process.StartAsync(kernel, new KernelProcessEvent { Id = DocGenerationProcessEvents.Step3PublishDocument, Data = docInfo });
        }

        private static void BuildProcess(ProcessBuilder processBuilder)
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
      
}
