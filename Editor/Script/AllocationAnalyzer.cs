using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Unity.ProjectAuditor.Editor
{
    [ScriptAnalyzer]
    public class AllocationAnalyzer : IInstructionAnalyzer
    {
        private static readonly ProblemDescriptor objectAllocationDescriptor = new ProblemDescriptor
        {
            id = 102002,
            description = "Object Allocation",
            type = string.Empty,
            method = string.Empty,
            area = "Memory",
            problem = "TODO",
            solution = "TODO"
        };

        private static readonly ProblemDescriptor arrayAllocationDescriptor = new ProblemDescriptor
        {
            id = 102003,
            description = "Array Allocation",
            type = string.Empty,
            method = string.Empty,
            area = "Memory",
            problem = "TODO",
            solution = "TODO"
        };

        public AllocationAnalyzer(ScriptAuditor auditor)
        {
            auditor.RegisterDescriptor(objectAllocationDescriptor);
            auditor.RegisterDescriptor(arrayAllocationDescriptor);
        }
        
        public ProjectIssue Analyze(MethodDefinition methodDefinition, Instruction inst)
        {
            if (inst.OpCode == OpCodes.Newobj)
            {
                var ctorMethod = (MethodReference)inst.Operand;
                var descriptor = objectAllocationDescriptor;
                var description = string.Format("{0} object allocation", ctorMethod.Name);
                
                var calleeNode = new CallTreeNode(descriptor.description);
            
                return new ProjectIssue
                (
                    descriptor,
                    description,
                    IssueCategory.ApiCalls,
                    calleeNode
                );                
            }
            else // OpCodes.Newarr
            {
                var type = (TypeReference)inst.Operand;
                var descriptor = arrayAllocationDescriptor;
                var description = string.Format("{0} array allocation", type.Name);
                
                var calleeNode = new CallTreeNode(descriptor.description);
        
                return new ProjectIssue
                (
                    descriptor,
                    description,
                    IssueCategory.ApiCalls,
                    calleeNode
                );                
            }
        }

        public IEnumerable<OpCode> GetOpCodes()
        {
            yield return OpCodes.Newobj;
            yield return OpCodes.Newarr;
        }
    }
}