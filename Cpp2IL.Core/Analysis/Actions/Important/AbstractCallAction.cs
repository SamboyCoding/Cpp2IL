using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public abstract class AbstractCallAction : BaseAction
    {
        public MethodReference? ManagedMethodBeingCalled;
        public List<IAnalysedOperand?>? Arguments;
        public LocalDefinition? ReturnedLocal;
        public LocalDefinition? InstanceBeingCalledOn;
        protected bool IsCallToSuperclassMethod;
        protected bool ShouldUseCallvirt;
        protected TypeReference? StaticMethodGenericTypeOverride;

        public AbstractCallAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
        }

        public override string? ToPsuedoCode()
        {
            if (ManagedMethodBeingCalled == null) 
                return "[instruction error - managed method being called is null]";
            
            var ret = new StringBuilder();

            if (ReturnedLocal != null)
                ret.Append(ReturnedLocal?.Type?.FullName).Append(' ').Append(ReturnedLocal?.Name).Append(" = ");

            if (!ManagedMethodBeingCalled.HasThis)
                ret.Append((StaticMethodGenericTypeOverride ?? ManagedMethodBeingCalled.DeclaringType).FullName);
            else if (InstanceBeingCalledOn?.Name == "this")
                ret.Append(IsCallToSuperclassMethod ? "base" : "this");
            else
                ret.Append(InstanceBeingCalledOn?.Name);
            
            if (ManagedMethodBeingCalled is MethodDefinition mDef)
            {
                if (mDef.Name.StartsWith("get_"))
                {
                    var unmanaged = mDef.AsUnmanaged();
                    var prop = unmanaged.DeclaringType!.Properties!.FirstOrDefault(p => p.Getter == unmanaged);

                    if (prop != null)
                        return ret.Append('.').Append(prop.Name).ToString();
                } else if (mDef.Name.StartsWith("set_"))
                {
                    var unmanaged = mDef.AsUnmanaged();
                    var prop = unmanaged.DeclaringType!.Properties!.FirstOrDefault(p => p.Setter == unmanaged);

                    if (prop != null && Arguments?.Count == 1)
                        return ret.Append('.').Append(prop.Name).Append(" = ").Append(Arguments[0].GetPseudocodeRepresentation()).ToString();
                }
            }

            ret.Append('.').Append(ManagedMethodBeingCalled?.Name);

            if (ManagedMethodBeingCalled is GenericInstanceMethod gim)
                ret.Append('<').Append(string.Join(", ", gim.GenericArguments)).Append('>');
            
            ret.Append('(');

            if (Arguments != null && Arguments.Count > 0)
            {
                ret.Append(string.Join(", ", GetReadableArguments()));
                ret.Append(')');

                if (ManagedMethodBeingCalled != null)
                {
                    ret.Append(" //(");
                    ret.Append(string.Join(", ", ManagedMethodBeingCalled!.Parameters.Select(p => $"{p.ParameterType.Name} {p.Name}")));
                    ret.Append(')');
                }
            }
            else
            {
                ret.Append(')');
            }

            return ret.ToString();
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (ManagedMethodBeingCalled == null)
                throw new TaintedInstructionException("Don't know what method is being called");
            
            if (ManagedMethodBeingCalled.Name == ".ctor")
            {
                //todo check if calling alternative constructor
                if (!(IsCallToSuperclassMethod && InstanceBeingCalledOn?.Name == "this"))
                    return Array.Empty<Mono.Cecil.Cil.Instruction>(); //Ignore ctors that aren't super calls, because we're allocating a new instance.
            }
            
            var result = GetILToLoadParams(context, processor);

            result.Add(processor.Create(ShouldUseCallvirt ? OpCodes.Callvirt : OpCodes.Call, processor.ImportReference(ManagedMethodBeingCalled.Resolve())));

            if (ManagedMethodBeingCalled.ReturnType.FullName == "System.Void")
                return result.ToArray();

            if (ReturnedLocal == null)
                throw new TaintedInstructionException("Returned local is null but non-void");

            result.Add(processor.Create(OpCodes.Stloc, ReturnedLocal.Variable));

            return result.ToArray();
        }

        public override string ToTextSummary()
        {
            string result;
            result = ManagedMethodBeingCalled?.HasThis == false
                ? $"[!] Calls static managed method {ManagedMethodBeingCalled?.FullName}"
                : $"[!] Calls managed method {ManagedMethodBeingCalled?.FullName} on instance {InstanceBeingCalledOn}";

            if (Arguments != null && Arguments.Count > 0)
                result += $" with arguments {Arguments.ToStringEnumerable()}";

            if (ReturnedLocal != null)
                result += $" and stores the result in {ReturnedLocal} in register rax";

            return result + "\n";
        }

        protected void CreateLocalForReturnType(MethodAnalysis context)
        {
            if (ManagedMethodBeingCalled?.ReturnType is { } returnType && returnType.FullName != "System.Void")
            {
                if (returnType is ArrayType arr)
                {
                    if (arr.ElementType != null)
                        returnType = ResolveGenericReturnTypeIfNeeded(arr.ElementType, context).MakeArrayType(arr.Rank);
                }
                else
                {
                    returnType = ResolveGenericReturnTypeIfNeeded(returnType, context);
                }

                var destReg = returnType.ShouldBeInFloatingPointRegister() ? "xmm0" : "rax";
                ReturnedLocal = context.MakeLocal(returnType, reg: destReg);

                //todo maybe improve?
                RegisterUsedLocal(ReturnedLocal);
            }
        }

        private TypeReference ResolveGenericReturnTypeIfNeeded(TypeReference returnType, MethodAnalysis context)
        {
            if (returnType is GenericParameter gp)
            {
                var methodInfo = MethodUtils.GetMethodInfoArg(ManagedMethodBeingCalled, context);

                if (methodInfo is ConstantDefinition {Value: GenericMethodReference gmr})
                {
                    returnType = GenericInstanceUtils.ResolveGenericParameterType(gp, gmr.Type, gmr.Method) ?? returnType;
                    StaticMethodGenericTypeOverride = gmr.Type;
                    
                    if(ManagedMethodBeingCalled.Resolve() == gmr.Method.Resolve())
                        ManagedMethodBeingCalled = gmr.Method;
                }
                else
                {
                    returnType = GenericInstanceUtils.ResolveGenericParameterType(gp, InstanceBeingCalledOn?.Type, ManagedMethodBeingCalled) ?? returnType;
                }
            }

            if (returnType is GenericInstanceType git)
            {
                try
                {
                    returnType = GenericInstanceUtils.ResolveMethodGIT(git, ManagedMethodBeingCalled!, InstanceBeingCalledOn?.Type, Arguments?.Select(a => a is LocalDefinition l ? l.Type : null).ToArray() ?? Array.Empty<TypeReference>());
                }
                catch (Exception)
                {
                    AddComment("Failed to resolve return type generic arguments.");
                }
            }

            return returnType;
        }

        protected void RegisterLocals()
        {
            Arguments?.Where(o => o is LocalDefinition).ToList().ForEach(o => RegisterUsedLocal((LocalDefinition) o));
            if (InstanceBeingCalledOn != null)
                RegisterUsedLocal(InstanceBeingCalledOn);
        }

        public List<Mono.Cecil.Cil.Instruction> GetILToLoadParams(MethodAnalysis context, ILProcessor processor, bool includeThis = true)
        {
            if (Arguments == null || Arguments.Count != ManagedMethodBeingCalled!.Parameters.Count)
                throw new TaintedInstructionException($"Couldn't get arguments, or actual count ({Arguments?.Count ?? -1}) is not equal to expected count ({ManagedMethodBeingCalled!.Parameters.Count})");

            var result = new List<Mono.Cecil.Cil.Instruction>();

            if (ManagedMethodBeingCalled.HasThis && includeThis)
                result.Add(context.GetILToLoad(InstanceBeingCalledOn ?? throw new TaintedInstructionException(), processor));

            foreach (var operand in Arguments)
            {
                if (operand == null)
                    throw new TaintedInstructionException($"Found null operand in Arguments: {Arguments.ToStringEnumerable()}");
                
                if (operand is LocalDefinition l)
                    result.Add(context.GetILToLoad(l, processor));
                else if (operand is ConstantDefinition c)
                    result.AddRange(c.GetILToLoad(context, processor));
                else
                    throw new TaintedInstructionException($"Don't know how to generate IL to load parameter of type {operand.GetType()}");
            }

            return result;
        }

        protected IEnumerable<string> GetReadableArguments()
        {
            foreach (var arg in Arguments)
            {
                if (arg is ConstantDefinition constantDefinition)
                    yield return constantDefinition.ToString();
                else
                    yield return (arg as LocalDefinition)?.Name ?? "null";
            }
        }

        public override bool IsImportant()
        {
            return true;
        }

        public override bool PseudocodeNeedsLinebreakBefore()
        {
            return true;
        }
    }
}