// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using ToolSelection.Models;

namespace ToolSelection.Services;

// Supports two auth modes against an Azure OpenAI / Azure AI Services embeddings endpoint:
//   1. API key  -> "api-key" header (set apiKey).
//   2. Entra ID -> "Authorization: Bearer" header (set credential, e.g. DefaultAzureCredential).
// Requests are bounded by a concurrency gate and retried on transient failures so the tool
// works against endpoints (e.g. a single AI Services deployment) that reset connections under
// the caller's unbounded fan-out.
public class EmbeddingService(HttpClient httpClient, string endpoint, string? apiKey, TokenCredential? credential = null)
{
    private static readonly string[] TokenScopes = ["https://cognitiveservices.azure.com/.default"];
    private const int MaxConcurrentRequests = 8;
    private const int MaxAttempts = 5;

    private readonly HttpClient _httpClient = httpClient;
    private readonly string _endpoint = endpoint;
    private readonly string? _apiKey = apiKey;
    private readonly TokenCredential? _credential = credential;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly SemaphoreSlim _concurrencyGate = new(MaxConcurrentRequests, MaxConcurrentRequests);
    private AccessToken? _cachedToken;

    public async Task<float[]> CreateEmbeddingsAsync(string input)
    {
        var requestBody = new EmbeddingRequest
        {
            Input = [input]
        };

        var json = JsonSerializer.Serialize(requestBody, SourceGenerationContext.Default.EmbeddingRequest);

        await _concurrencyGate.WaitAsync();

        try
        {
            for (int attempt = 1; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrEmpty(_apiKey))
                {
                    request.Headers.Add("api-key", _apiKey);
                }
                else
                {
                    var token = await GetTokenAsync();
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                try
                {
                    using var response = await _httpClient.SendAsync(request);
                    var statusCode = (int)response.StatusCode;

                    if (attempt < MaxAttempts && (statusCode == 429 || statusCode >= 500))
                    {
                        await Task.Delay(GetRetryDelay(attempt, response));

                        continue;
                    }

                    response.EnsureSuccessStatusCode();

                    var responseContent = await response.Content.ReadAsStringAsync();
                    var embeddingResponse = JsonSerializer.Deserialize(responseContent, SourceGenerationContext.Default.EmbeddingResponse);

                    if (embeddingResponse?.Error != null)
                    {
                        throw new InvalidOperationException($"API error: {embeddingResponse.Error.Type} - {embeddingResponse.Error.Message}");
                    }

                    if (embeddingResponse?.Data == null || embeddingResponse.Data.Length == 0)
                    {
                        throw new InvalidOperationException($"No embedding data returned from API. Response: {responseContent}");
                    }

                    return embeddingResponse.Data[0].Embedding;
                }
                catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex))
                {
                    await Task.Delay(GetRetryDelay(attempt, null));
                }
            }
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        IOException => true,
        TaskCanceledException => true,
        HttpRequestException { StatusCode: null } => true, // connection-level failure (reset/EOF)
        HttpRequestException hre => (int)hre.StatusCode! == 429 || (int)hre.StatusCode! >= 500,
        _ => false,
    };

    private static TimeSpan GetRetryDelay(int attempt, HttpResponseMessage? response)
    {
        if (response?.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        // Exponential backoff with jitter: ~0.25s, 0.5s, 1s, 2s ...
        var baseMs = 250 * Math.Pow(2, attempt - 1);

        return TimeSpan.FromMilliseconds(baseMs + Random.Shared.Next(0, 250));
    }

    private async Task<string> GetTokenAsync()
    {
        if (_credential is null)
        {
            throw new InvalidOperationException("No token credential configured for Entra ID authentication.");
        }

        if (_cachedToken is { } cached && cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return cached.Token;
        }

        await _tokenLock.WaitAsync();

        try
        {
            if (_cachedToken is { } current && current.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return current.Token;
            }

            var token = await _credential.GetTokenAsync(new TokenRequestContext(TokenScopes), CancellationToken.None);
            _cachedToken = token;

            return token.Token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
