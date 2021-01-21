using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis.Actions;
using LibCpp2IL;
using Mono.Cecil;

namespace Cpp2IL.Analysis.ResultModels
{
    public class MethodAnalysis
    {
        public readonly List<LocalDefinition> Locals = new List<LocalDefinition>();
        public readonly List<ConstantDefinition> Constants = new List<ConstantDefinition>();
        public readonly List<BaseAction> Actions = new List<BaseAction>();
        public readonly List<ulong> IdentifiedIfStatementStarts = new List<ulong>();
        public readonly List<ulong> ProbableLoopStarts = new List<ulong>();

        private ConstantDefinition EmptyRegConstant;

        internal ulong MethodStart;
        internal ulong AbsoluteMethodEnd;

        private MethodDefinition _method;

        public readonly List<LocalDefinition> FunctionArgumentLocals = new List<LocalDefinition>();
        
        private readonly Dictionary<string, IAnalysedOperand> RegisterData = new Dictionary<string, IAnalysedOperand>();
        public readonly Dictionary<int, LocalDefinition> StackStoredLocals = new Dictionary<int, LocalDefinition>();
        public readonly Stack<IAnalysedOperand> Stack = new Stack<IAnalysedOperand>();

        internal MethodAnalysis(MethodDefinition method, ulong methodStart, ulong initialMethodEnd)
        {
            _method = method;
            MethodStart = methodStart;
            AbsoluteMethodEnd = initialMethodEnd;
            EmptyRegConstant = MakeConstant(typeof(int), 0, "0");

            var args = method.Parameters.ToList();
            //Set up parameters in registers & as locals.
            if (!LibCpp2IlMain.ThePe!.is32Bit)
            {
                var regList = new List<string> {"rcx", "rdx", "r8", "r9"};

                if (!method.IsStatic)
                    FunctionArgumentLocals.Add(MakeLocal(method.DeclaringType, "this", regList.RemoveAndReturn(0)));

                while (args.Count > 0 && regList.Count > 0)
                {
                    var arg = args.RemoveAndReturn(0);
                    var dest = regList.RemoveAndReturn(0);

                    FunctionArgumentLocals.Add(MakeLocal(arg.ParameterType.Resolve(), arg.Name, dest));
                }
            } else if (!method.IsStatic)
            {
                //32-bit, instance method
                FunctionArgumentLocals.Add(MakeLocal(method.DeclaringType, "this"));
                Stack.Push(FunctionArgumentLocals.First());
            }

            //This is executed regardless of if we're x86 or x86-64. In x86-64, the first 4 args go in registers, and the rest go on the stack
            //in x86, everything is on the stack.
            while (args.Count > 0)
            {
                //Push remainder to stack
                var arg = args.RemoveAndReturn(0);
                var localDefinition = MakeLocal(arg.ParameterType.Resolve(), arg.Name);
                Stack.Push(localDefinition);
                FunctionArgumentLocals.Add(localDefinition);
            }
        }

        public bool IsVoid() => _method.ReturnType?.FullName == "System.Void";

        public bool IsConstructor() => _method.IsConstructor;

        public TypeDefinition GetTypeOfThis() => _method.DeclaringType;

        public bool IsStatic() => _method.IsStatic;


        public override string ToString()
        {
            return $"Method Analysis for {_method.FullName}";
        }

        public LocalDefinition MakeLocal(TypeReference type, string? name = null, string? reg = null)
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

        //These two are for mathematical operations on the stack.

        public void PushEmptyStackFrames(int count)
        {
            for (var i = 0; i < count; i++)
                Stack.Push(EmptyRegConstant);
        }

        public void PopStackFramesAndDiscord(int count)
        {
            for (var i = 0; i < count; i++)
                Stack.TryPop(out _);
        }

        public void SetRegContent(string reg, IAnalysedOperand? content)
        {
            RegisterData[reg] = content;
        }

        public void ZeroRegister(string reg)
        {
            SetRegContent(reg, EmptyRegConstant);
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