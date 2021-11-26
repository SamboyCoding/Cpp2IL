using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Cpp2IL.Core.Utils
{
    public static class CecilUtils
    {
        private static readonly FieldInfo EtypeField = typeof(TypeReference).GetField("etype", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly FieldInfo PositionField = typeof(GenericParameter).GetField("position", BindingFlags.NonPublic | BindingFlags.Instance)!;
        
        public static bool HasAnyGenericCrapAnywhere(TypeReference reference)
        {
            if (reference is GenericParameter)
                return true;

            if (reference is GenericInstanceType git)
            {
                //check for e.g. List<List<T>> 
                return git.GenericArguments.Any(HasAnyGenericCrapAnywhere);
            }

            if (reference is TypeSpecification typeSpec)
                //Pointers, byrefs, etc
                return HasAnyGenericCrapAnywhere(typeSpec.ElementType);

            return reference.HasGenericParameters;
        }

        public static bool HasAnyGenericCrapAnywhere(MethodReference reference, bool checkDeclaringTypeParamsAndReturn = true)
        {
            if (checkDeclaringTypeParamsAndReturn && HasAnyGenericCrapAnywhere(reference.DeclaringType))
                return true;

            if (checkDeclaringTypeParamsAndReturn && HasAnyGenericCrapAnywhere(reference.ReturnType))
                return true;

            if (checkDeclaringTypeParamsAndReturn && reference.Parameters.Any(p => HasAnyGenericCrapAnywhere(p.ParameterType)))
                return true;

            if (reference.HasGenericParameters)
                return true;

            if (reference is GenericInstanceMethod gim)
                return gim.GenericArguments.Any(HasAnyGenericCrapAnywhere);

            return false;
        }

        public static TypeReference ImportTypeButCleanly(this ModuleDefinition module, TypeReference reference)
        {
            if (reference is GenericParameter)
                //These two lines are what cecil is missing. It's so simple :/
                return reference;

            if (reference is GenericInstanceType git)
                return module.ImportTypeButCleanly(git.ElementType).MakeGenericInstanceType(git.GenericArguments.Select(module.ImportTypeButCleanly).ToArray());

            if (reference is ArrayType at)
                return module.ImportTypeButCleanly(at.ElementType).MakeArrayType();

            if (reference is ByReferenceType brt)
                return module.ImportTypeButCleanly(brt.ElementType).MakeByReferenceType();

            if (reference is PointerType pt)
                return module.ImportTypeButCleanly(pt.ElementType).MakePointerType();

            if (reference is TypeDefinition td)
                return module.ImportReference(td);

            if (reference.GetType() == typeof(TypeReference))
                return module.ImportReference(reference);

            throw new NotSupportedException($"Support for importing {reference} of type {reference.GetType()} is not implemented");
        }

        public static ParameterDefinition ImportParameterButCleanly(this ModuleDefinition module, ParameterDefinition param)
        {
            param.ParameterType = HasAnyGenericCrapAnywhere(param.ParameterType) ? module.ImportTypeButCleanly(param.ParameterType) : module.ImportReference(param.ParameterType);
            return param;
        }

        public static MethodReference ImportMethodButCleanly(this ModuleDefinition module, MethodReference method)
        {
            var returnType = module.ImportTypeButCleanly(method.ReturnType);
            var declaringType = module.ImportTypeButCleanly(method.DeclaringType);
            var gArgs = (method as GenericInstanceMethod)?.GenericArguments.Select(module.ImportTypeButCleanly).ToList();
            var methodParams = method.Parameters.Select(module.ImportParameterButCleanly).ToList();

            var ret = new MethodReference(method.Name, returnType, declaringType);
            if (gArgs != null)
            {
                var gMtd = new GenericInstanceMethod(ret);
                gArgs.ForEach(gMtd.GenericArguments.Add);
                ret = gMtd;
            }

            methodParams.ForEach(ret.Parameters.Add);

            return ret;
        }

        public static FieldReference ImportFieldButCleanly(this ModuleDefinition module, FieldReference field)
        {
            var declaringType = module.ImportTypeButCleanly(field.DeclaringType);
            var fieldType = module.ImportTypeButCleanly(field.FieldType);

            return new(field.Name, fieldType, declaringType);
        }

        public static TypeReference ImportTypeButCleanly(this ILProcessor processor, TypeReference reference) => processor.Body.Method.Module.ImportTypeButCleanly(reference);
        
        public static MethodReference ImportMethodButCleanly(this ILProcessor processor, MethodReference reference) => processor.Body.Method.Module.ImportMethodButCleanly(reference);

        public static void SetEType(this TypeReference tr, CecilEType eType) => EtypeField.SetValue(tr, (byte) eType);

        public static CecilEType GetEType(this TypeReference tr) => (CecilEType) (byte) EtypeField.GetValue(tr);
        
        public static void SetPosition(this GenericParameter gp, int position) => PositionField.SetValue(gp, position);

        public static int GetPosition(this GenericParameter gp) => (int) PositionField.GetValue(gp);
    }
}