// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ClientModel.Primitives;
using System.Text.Json.Nodes;

namespace Azure.Mcp.Tools.Monitor.Services;

/// <summary>
/// The single bridge between a CloudHealth SDK model and the wire JSON the graph-edit planner patches.
/// </summary>
/// <remarks>
/// The whole write path rests on this pair being lossless: the planner reads <see cref="Wire{T}"/> output,
/// merge-patches it, and the runner hands the result back to <see cref="Read{T}"/> to PUT. If the beta SDK
/// dropped a property it does not recognise, a PUT would silently delete server state the caller never
/// mentioned — <c>HealthModelSignalDefinitionProperties</c> is an abstract polymorphic bag whose
/// <c>signalKind</c> discriminator is not on the base type, so that risk is real and is pinned by test.
/// Reading goes through the source-generated <c>AzureResourceManagerCloudHealthContext</c> so the path
/// stays AOT-safe.
/// </remarks>
internal static class HealthModelWireCodec
{
    internal static readonly ModelReaderWriterOptions WireFormat = new("J");

    internal static JsonObject Wire<T>(T data) where T : IPersistableModel<T> =>
        JsonNode.Parse(data.Write(WireFormat).ToMemory().Span)!.AsObject();

    internal static T Read<T>(BinaryData payload) =>
        ModelReaderWriter.Read<T>(
            payload, WireFormat, ResourceManager.CloudHealth.AzureResourceManagerCloudHealthContext.Default)!;
}
