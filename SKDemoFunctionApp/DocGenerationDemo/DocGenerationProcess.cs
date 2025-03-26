using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace SKDemoFunctionApp.DocGenerationDemo
{
    public static class DocGenerationProcessEvents
    {
        public static string Start = "dgp.start";
        public static string Step1DocumentInfo = "dgp.step1documentinfo";
        public static string Step2CreateDocument = "dgp.step2createdocument";
        public static string Step3PublishDocument = "dgp.step3publishdocument";
    }

    #region Steps
    public class Step1DocumentInfo : KernelProcessStep
    {
        [KernelFunction]
        public async Task<DocumentGenerationInfo> StartStep(Kernel kernel, KernelProcessStepContext context, DocumentGenerationInfo docInfo)
        {
            Console.WriteLine($"Step 1 - Document Info executing");

            string prompt = $"write fictional information for {docInfo.Subject} and limit to 100 words";

            ChatHistory chatHistory = new ChatHistory(prompt);

            IChatCompletionService chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
            var chatResponse = await chatCompletionService.GetChatMessageContentAsync(chatHistory);

            docInfo.Information = chatResponse.Content!;

            //DocumentGenerationInfoRepo.AddUpdateDocumentInfo(docInfo);

            DocumentGenerationInfoRepo repo = new DocumentGenerationInfoRepo();
            var res = await repo.AddUpdate(docInfo);

            Console.WriteLine($"Step 1 - Document Info execution complete");

            return docInfo;
        }
    }

    public class Step2CreateDocument : KernelProcessStep
    {
        //private DocState _state = new DocState();

        [KernelFunction]
        public async Task<DocumentGenerationInfo> StartStepAsync(Kernel kernel, KernelProcessStepContext context, DocumentGenerationInfo docInfo)
        {
            Console.WriteLine($"Step 2 Create document executing");

            string prompt = $"write fitional customer facing documentation for product {docInfo.Subject} and limit to 200 words";

            ChatHistory chatHistory = new ChatHistory(prompt);

            IChatCompletionService chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
            var chatResponse = await chatCompletionService.GetChatMessageContentAsync(chatHistory);

            docInfo.GeneratedText = chatResponse.Content!;

            //DocumentGenerationInfoRepo.AddUpdateDocumentInfo(docInfo);

            DocumentGenerationInfoRepo docRepo = new DocumentGenerationInfoRepo();
            await docRepo.AddUpdate(docInfo);

            Console.WriteLine($"Step 2 Create document execution complete");

            return docInfo;
        }
       
    }

    public class Step3PublishDocument : KernelProcessStep
    {
        [KernelFunction]
        public async Task PublishDocumentation(DocumentGenerationInfo docInfo)
        {
            Console.WriteLine($"Step 3 Publish document executing");

            // Only publish the documentation if it has been approved
            if (docInfo.IsApproved)
            {
                docInfo.IsPublished = true;
                // For example purposes we just write the generated docs to the console
                Console.WriteLine($"\tDocument published successfully");
            }
            else
            {
                docInfo.IsPublished = false;
                Console.WriteLine($"\tDocument not published as not approved");
            }

            DocumentGenerationInfoRepo docRepo = new DocumentGenerationInfoRepo();
            await docRepo.AddUpdate(docInfo);

            Console.WriteLine($"Step 3 Publish document execution complete");
        }
    }

    #endregion

    //POCO
    public class DocumentGenerationInfo
    {
        public DocumentGenerationInfo()
        {
            id = Guid.NewGuid().ToString();
            docid = id;
        }

        public string id { get; set; }
        public string docid { get; set; }
        public string Subject { get; set; }
        public string Information { get; set; }
        public string GeneratedText { get; set; }
        public bool IsApproved { get; set; }
        public bool IsPublished { get; set; }
    }

    public class DocumentGenerationInfoRepo
    {
        public async Task<DocumentGenerationInfo> AddUpdate(DocumentGenerationInfo docInfo)
        {
            using (CosmosClient cosmosClient = new CosmosClient("AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="))
            {
                Database database = cosmosClient.GetDatabase("IntelligentApp");
                Container container = database.GetContainer("SKDocProcess");

                if (string.IsNullOrEmpty(docInfo.docid))
                {
                    docInfo.id = Guid.NewGuid().ToString();
                    docInfo.docid = docInfo.id;

                }
                var response = await container.UpsertItemAsync<DocumentGenerationInfo>(docInfo, new PartitionKey(docInfo.docid));

                return response.Resource;
            }
        }

        public async Task<DocumentGenerationInfo> Get(string id)
        {
            using (CosmosClient cosmosClient = new CosmosClient("AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="))
            {
                Database database = cosmosClient.GetDatabase("IntelligentApp");
                Container container = database.GetContainer("SKDocProcess");

                var response = await container.ReadItemAsync<DocumentGenerationInfo>(id, new PartitionKey(id));

                return response.Resource;
            }
        }

    }
}
