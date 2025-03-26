using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel;
using static ProcessAutomation.GenerateDocumentationStep;
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
            //DemoTestProcess demoProcess = new DemoTestProcess();
            //await demoProcess.Test();

            //return;


            // Create the process builder
            ProcessBuilder processBuilder = new ProcessBuilder("DocumentationGeneration");

            //// Add the steps
            //var step1 = processBuilder.AddStepFromType<Step1>();
            //var step2 = processBuilder.AddStepFromType<Step2>();
            //var step3 = processBuilder.AddStepFromType<Step3>();

            //// Orchestrate the events
            //processBuilder
            //    .OnInputEvent(DemoProcessEvents.Start)
            //    .SendEventTo(new(step1));

            //processBuilder
            //    .OnInputEvent(DemoProcessEvents.Step2)
            //    .SendEventTo(new(step2));

            //step1
            //    .OnFunctionResult()
            //    .SendEventTo(new(step2));

            //step2
            //    .OnFunctionResult()
            //    .SendEventTo(new(step3));

            BuildProcess(processBuilder);



            // Configure the kernel with your LLM connection details
            Kernel kernel = Kernel.CreateBuilder()
                .AddAzureOpenAIChatCompletion("sparksgpt432k", "https://sparksopenaiauseast.openai.azure.com/", "28ef63af825a47f6a3d5f008abaaa08f")
                .Build();

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
        }

        private static void BuildProcess(ProcessBuilder processBuilder)
        {
            //doc generation steps            
            var step1DocumentInfo = processBuilder.AddStepFromType<Step1DocumentInfo>();
            var step2CreateDocument = processBuilder.AddStepFromType<Step2CreateDocument>();

            // Orchestrate the events
            processBuilder
                .OnInputEvent(DocGenerationProcessEvents.Start)
                .SendEventTo(new(step1DocumentInfo));

            //ends

            step1DocumentInfo
                .OnFunctionResult()
                .SendEventTo(new(step2CreateDocument));


            step2CreateDocument
                .OnEvent(DocGenerationProcessEvents.Step1)
                .SendEventTo(new(step1DocumentInfo));
        }
    }

    public class DemoTestProcess
    {
        public async Task Test()
        {
            // Create the process builder
            ProcessBuilder processBuilder = new("DocumentationGeneration");

            // Add the steps
            var infoGatheringStep = processBuilder.AddStepFromType<GatherProductInfoStep>();
            var docsGenerationStep = processBuilder.AddStepFromType<GenerateDocumentationStep>();
            var docsProofreadStep = processBuilder.AddStepFromType<ProofreadStep>();
            var docsPublishStep = processBuilder.AddStepFromType<PublishDocumentationStep>();

            // Orchestrate the events
            processBuilder
                .OnInputEvent("Start")
                .SendEventTo(new(infoGatheringStep));

            // When external human approval event comes in, route it to the 'isApproved' parameter of the docsPublishStep
            processBuilder
                .OnInputEvent("HumanApprovalResponse")
                .SendEventTo(new(docsPublishStep, parameterName: "isApproved"));

            infoGatheringStep
                .OnFunctionResult()
                .SendEventTo(new(docsGenerationStep, functionName: "GenerateDocumentation"));

            docsGenerationStep
                .OnEvent("DocumentationGenerated")
                .SendEventTo(new(docsProofreadStep));

            docsProofreadStep
                .OnEvent("DocumentationRejected")
                .SendEventTo(new(docsGenerationStep, functionName: "ApplySuggestions"));

            docsProofreadStep
                .OnEvent("DocumentationApproved")
                .SendEventTo(new(docsPublishStep, parameterName: "docs"));
            
            // Configure the kernel with your LLM connection details
            Kernel kernel = Kernel.CreateBuilder()
                .AddAzureOpenAIChatCompletion("sparksgpt432k", "https://sparksopenaiauseast.openai.azure.com/", "28ef63af825a47f6a3d5f008abaaa08f")
                .Build();
            //gpt-4o_sparks

            // Build and run the process
            var process = processBuilder.Build();
            await process.StartAsync(kernel, new KernelProcessEvent { Id = "Start", Data = "Contoso GlowBrew" });



            // Restart the process with approval for publishing the documentation.
            await process.StartAsync(kernel, new KernelProcessEvent { Id = "HumanApprovalResponse", Data = true });
        }
    }


    // A process step to gather information about a product
    public class GatherProductInfoStep : KernelProcessStep
    {
        [KernelFunction]
        public string GatherProductInformation(string productName)
        {
            Console.WriteLine($"{nameof(GatherProductInfoStep)}:\n\tGathering product information for product named {productName}");

            // For example purposes we just return some fictional information.
            return
                """
            Product Description:
            GlowBrew is a revolutionary AI driven coffee machine with industry leading number of LEDs and programmable light shows. The machine is also capable of brewing coffee and has a built in grinder.

            Product Features:
            1. **Luminous Brew Technology**: Customize your morning ambiance with programmable LED lights that sync with your brewing process.
            2. **AI Taste Assistant**: Learns your taste preferences over time and suggests new brew combinations to explore.
            3. **Gourmet Aroma Diffusion**: Built-in aroma diffusers enhance your coffee's scent profile, energizing your senses before the first sip.

            Troubleshooting:
            - **Issue**: LED Lights Malfunctioning
                - **Solution**: Reset the lighting settings via the app. Ensure the LED connections inside the GlowBrew are secure. Perform a factory reset if necessary.
            """;
        }
    }

    // A process step to generate documentation for a product
    public class GenerateDocumentationStep : KernelProcessStep<GeneratedDocumentationState>
    {
        private GeneratedDocumentationState _state = new();

        private string systemPrompt =
                """
            Your job is to write high quality and engaging customer facing documentation for a new product from Contoso. You will be provide with information
            about the product in the form of internal documentation, specs, and troubleshooting guides and you must use this information and
            nothing else to generate the documentation. If suggestions are provided on the documentation you create, take the suggestions into account and
            rewrite the documentation. Make sure the product sounds amazing.
            """;

        // Called by the process runtime when the step instance is activated. Use this to load state that may be persisted from previous activations.
        override public ValueTask ActivateAsync(KernelProcessStepState<GeneratedDocumentationState> state)
        {
            this._state = state.State!;
            this._state.ChatHistory ??= new ChatHistory(systemPrompt);

            return base.ActivateAsync(state);
        }

        [KernelFunction]
        public async Task<string> GenerateDocumentationAsync(Kernel kernel, KernelProcessStepContext context, string productInfo)
        {
            Console.WriteLine($"{nameof(GenerateDocumentationStep)}:\n\tGenerating documentation for provided productInfo...");

            // Add the new product info to the chat history
            this._state.ChatHistory!.AddUserMessage($"Product Info:\n\n{productInfo}");

            // Get a response from the LLM
            IChatCompletionService chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
            var generatedDocumentationResponse = await chatCompletionService.GetChatMessageContentAsync(this._state.ChatHistory!);

            await context.EmitEventAsync("DocumentationGenerated", generatedDocumentationResponse.Content!.ToString());

            return generatedDocumentationResponse.Content;
        }

        [KernelFunction]
        public async Task<string> ApplySuggestionsAsync(Kernel kernel, KernelProcessStepContext context, string suggestions)
        {
            Console.WriteLine($"{nameof(GenerateDocumentationStep)}:\n\tRewriting documentation with provided suggestions...");

            // Add the new product info to the chat history
            this._state.ChatHistory!.AddUserMessage($"Rewrite the documentation with the following suggestions:\n\n{suggestions}");

            // Get a response from the LLM
            IChatCompletionService chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
            var generatedDocumentationResponse = await chatCompletionService.GetChatMessageContentAsync(this._state.ChatHistory!);

            await context.EmitEventAsync("DocumentationGenerated", generatedDocumentationResponse.Content!.ToString());

            return generatedDocumentationResponse.Content;
        }

        public class GeneratedDocumentationState
        {
            public ChatHistory? ChatHistory { get; set; }
        }
    }

    // A process step to publish documentation
    public class PublishDocumentationStep : KernelProcessStep
    {
        [KernelFunction]
        public void PublishDocumentation(string docs, bool isApproved)
        {
            // Only publish the documentation if it has been approved
            if (isApproved)
            {
                // For example purposes we just write the generated docs to the console
                Console.WriteLine($"{nameof(PublishDocumentationStep)}:\n\tPublishing product documentation:\n\n{docs}");
            }
        }
    }

    // A process step to proofread documentation
    public class ProofreadStep : KernelProcessStep
    {
        [KernelFunction]
        public async Task<string> ProofreadDocumentationAsync(Kernel kernel, KernelProcessStepContext context, string documentation)
        {
            Console.WriteLine($"{nameof(ProofreadDocumentationAsync)}:\n\tProofreading documentation...");

            var systemPrompt =
                """
        Your job is to proofread customer facing documentation for a new product from Contoso. You will be provided with proposed documentation
        for a product and you must do the following things. You will respond in json format outputing Suggestions if any, MeetsExpectations if document meets expectation or not
        and Explanation why document does not meet expectation if it does not meet expectation.

        1. Determine if the documentation is passes the following criteria:
            1. Documentation must use a professional tone.
            1. Documentation should be free of spelling or grammar mistakes.
            1. Documentation should be free of any offensive or inappropriate language.
            1. Documentation should be technically accurate.
        2. If the documentation does not pass 1, you must write detailed feedback of the changes that are needed to improve the documentation. 
        """;

            ChatHistory chatHistory = new ChatHistory(systemPrompt);
            chatHistory.AddUserMessage(documentation);

            // Use structured output to ensure the response format is easily parsable
            OpenAIPromptExecutionSettings settings = new OpenAIPromptExecutionSettings();
            settings.ResponseFormat = typeof(ProofreadingResponse);

            IChatCompletionService chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
            //var proofreadResponse = await chatCompletionService.GetChatMessageContentAsync(chatHistory, executionSettings: settings);
            var proofreadResponse = await chatCompletionService.GetChatMessageContentAsync(chatHistory);
            var formattedResponse = JsonSerializer.Deserialize<ProofreadingResponse>(proofreadResponse.Content!.ToString());

            Console.WriteLine($"\n\tGrade: {(formattedResponse!.MeetsExpectations ? "Pass" : "Fail")}\n\tExplanation: {formattedResponse.Explanation}\n\tSuggestions: {string.Join("\n\t\t", formattedResponse.Suggestions)}");

            if (formattedResponse.MeetsExpectations)
            {
                await context.EmitEventAsync("DocumentationApproved", data: documentation);

                // Emit event to external pubsub to trigger human in the loop approval.
                await context.EmitEventAsync("HumanApprovalRequired", data: documentation, visibility: KernelProcessEventVisibility.Public);
            }
            else
            {
                await context.EmitEventAsync("DocumentationRejected", data: new { Explanation = formattedResponse.Explanation, Suggestions = formattedResponse.Suggestions });
            }

            return proofreadResponse.Content;
        }

        // A class 
        public class ProofreadingResponse
        {
            [Description("Specifies if the proposed documentation meets the expected standards for publishing.")]
            public bool MeetsExpectations { get; set; }

            [Description("An explanation of why the documentation does or does not meet expectations.")]
            public string Explanation { get; set; } = "";

            [Description("A list of suggestions, may be empty if there no suggestions for improvement.")]
            public List<string> Suggestions { get; set; } = new();

            //[Description("A list of suggestions, may be empty if there no suggestions for improvement.")]
            //public string[] Suggestions { get; set; }
        }
    }
}
