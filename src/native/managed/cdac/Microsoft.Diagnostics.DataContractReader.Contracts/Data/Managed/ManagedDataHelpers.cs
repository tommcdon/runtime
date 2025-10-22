// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Microsoft.Diagnostics.DataContractReader.Contracts;

namespace Microsoft.Diagnostics.DataContractReader.Data;

public static class ManagedDataHelpers
{
    public static Dictionary<string, uint> ParseOffsets(
        Target target,
        string typeName,
        string typeNamespace)
    {
        IRuntimeTypeSystem rts = target.Contracts.RuntimeTypeSystem;
        IEcmaMetadata ecmaMetadata = target.Contracts.EcmaMetadata;

        Dictionary<string, uint> fieldOffsets = [];

        if (!TryGetTypeByName(target, typeName, typeNamespace, out TypeHandle typeHandle, out Contracts.ModuleHandle moduleHandle))
            throw new InvalidOperationException($"Type {typeNamespace}.{typeName} not found in any loaded module.");

        foreach (TargetPointer fieldDescPtr in rts.GetFieldDescs(typeHandle))
        {
            // only read instance fields
            if (rts.IsFieldDescStatic(fieldDescPtr) || rts.IsFieldDescThreadStatic(fieldDescPtr))
                continue;

            uint token = rts.GetFieldDescMemberDef(fieldDescPtr);
            FieldDefinitionHandle fieldHandle = (FieldDefinitionHandle)MetadataTokens.Handle((int)token);
            MetadataReader mdReader = ecmaMetadata.GetMetadata(moduleHandle)!;
            FieldDefinition fieldDef = mdReader.GetFieldDefinition(fieldHandle);

            uint fieldOffset = rts.GetFieldDescOffset(fieldDescPtr, fieldDef);
            string fieldName = mdReader.GetString(fieldDef.Name);

            fieldOffsets[fieldName] = fieldOffset;
        }

        return fieldOffsets;
    }

    private static bool TryGetTypeByName(
        Target target,
        string typeName,
        string typeNamespace,
        out TypeHandle typeHandle,
        out Contracts.ModuleHandle moduleHandle)
    {
        typeHandle = default;
        moduleHandle = default;

        ILoader loader = target.Contracts.Loader;
        IRuntimeTypeSystem rts = target.Contracts.RuntimeTypeSystem;

        TargetPointer appDomainPointer = target.ReadGlobalPointer(Constants.Globals.AppDomain);
        TargetPointer appDomain = target.ReadPointer(appDomainPointer);

        foreach (Contracts.ModuleHandle module in loader.GetModuleHandles(
            appDomain,
            AssemblyIterationFlags.IncludeLoaded | AssemblyIterationFlags.IncludeExecution))
        {
            TypeHandle type = rts.GetTypeByNameAndModule(typeName, typeNamespace, module);
            if (!type.IsNull)
            {
                typeHandle = type;
                moduleHandle = module;
                return true;
            }
        }
        return false;
    }
}
