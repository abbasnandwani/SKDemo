using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcessAutomation
{
    public class Step1 : KernelProcessStep
    {
        [KernelFunction]
        public string StartStep(Kernel kernel, KernelProcessStepContext context,  string productName)
        {
            Console.WriteLine($"This is Step1 for {productName}");
            
            return $"{productName} processed.";
        }
       
    }

    public class Step2 : KernelProcessStep
    {
        [KernelFunction]
        public void StartStep()
        {
            Console.WriteLine($"This is Step2");
        }
    }

    public class Step3 : KernelProcessStep
    {
        [KernelFunction]
        public void StartStep()
        {
            Console.WriteLine($"This is Step3");
        }
    }

    public static class DemoProcessEvents
    {
        public static string Start = "Start";
        public static string Step1 = "Step1";
        public static string Step2 = "Step2";
        public static string Step3 = "Step3";
    }
}
