using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis.Actions;
using Mono.Cecil;

namespace Cpp2IL.Analysis.ResultModels
{
    public class MethodAnalysis
    {
        public readonly List<LocalDefinition> Locals = new List<LocalDefinition>();
        public readonly List<ConstantDefinition> Constants = new List<ConstantDefinition>();
        public readonly List<BaseAction> Actions = new List<BaseAction>();
        public readonly List<ulong> IdentifiedIfStatementStarts = new List<ulong>();

        private ConstantDefinition EmptyRegConstant;
        
        internal ulong MethodStart;
        internal ulong AbsoluteMethodEnd;

        private MethodDefinition _method;

        private readonly Dictionary<string, IAnalysedOperand> RegisterData = new Dictionary<string, IAnalysedOperand>();
        private readonly Dictionary<int, IAnalysedOperand> StackData = new Dictionary<int, IAnalysedOperand>();

        internal MethodAnalysis(MethodDefinition method, ulong methodStart, ulong initialMethodEnd)
        {
            _method = method;
            MethodStart = methodStart;
            AbsoluteMethodEnd = initialMethodEnd;
            EmptyRegConstant = MakeConstant(typeof(int), 0, "0");
            
            //Set up parameters in registers & as locals.
            var regList = new List<string> {"rcx", "rdx", "r8", "r9"};

            if (!method.IsStatic)
                MakeLocal(method.DeclaringType, "this", regList.RemoveAndReturn(0));

            var args = method.Parameters.ToList();
            while (args.Count > 0 && regList.Count > 0)
            {
                var arg = args.RemoveAndReturn(0);
                var dest = regList.RemoveAndReturn(0);

                MakeLocal(arg.ParameterType.Resolve(), arg.Name, dest);
            }

            var stackIdx = 0;
            while (args.Count > 0)
            {
                //Push remainder to stack
                var arg = args.RemoveAndReturn(0);
                PushToStack(MakeLocal(arg.ParameterType.Resolve(), arg.Name), stackIdx);
                stackIdx += (int) Utils.GetSizeOfObject(arg.ParameterType);
            }
        }

        public override string ToString()
        {
            return $"Method Analysis for {_method.FullName}";
        }

        public LocalDefinition MakeLocal(TypeDefinition? type, string? name = null, string? reg = null)
        {
            var local = new LocalDefinition
            {
                Name = name ?? $"local{Locals.Count}",
                Type = type
            };

            Locals.Add(local);
            
            if (reg != null)
                RegisterData[reg] = local;

            return local;
        }

        public ConstantDefinition MakeConstant(Type type, object value, string? name = null, string? reg = null)
        {
            var constant = new ConstantDefinition
            {
                Name = name ?? $"constant{Constants.Count}",
                Type = type,
                Value = value
            };
            
            Constants.Add(constant);

            if (reg != null)
                RegisterData[reg] = constant;

            return constant;
        }

        public void SetRegContent(string reg, IAnalysedOperand? content)
        {
            RegisterData[reg] = content;
        }

        public void ZeroRegister(string reg)
        {
            SetRegContent(reg, EmptyRegConstant);
        }

        public IAnalysedOperand PushToStack(IAnalysedOperand operand, int pos)
        {
            StackData[pos] = operand;
            return operand;
        }

        public IAnalysedOperand? GetOperandInRegister(string reg)
        {
            if (!RegisterData.TryGetValue(reg, out var result))
                return null;
            
            return result;
        }

        public LocalDefinition? GetLocalInReg(string reg)
        {
            if (!RegisterData.TryGetValue(reg, out var result))
                return null;

            if (!(result is LocalDefinition local)) return null;

            return local;
        }
        
        public ConstantDefinition? GetConstantInReg(string reg)
        {
            if (!RegisterData.TryGetValue(reg, out var result))
                return null;

            if (!(result is ConstantDefinition constant)) return null;

            return constant;
        }

        public bool IsEmptyRegArg(IAnalysedOperand analysedOperand)
        {
            return analysedOperand == EmptyRegConstant;
        }
    }
}