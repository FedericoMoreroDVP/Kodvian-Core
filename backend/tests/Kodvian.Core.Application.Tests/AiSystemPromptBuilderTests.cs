using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Infrastructure.Services;

namespace Kodvian.Core.Application.Tests;

public sealed class AiSystemPromptBuilderTests
{
    private static readonly AiToolDefinitionDto Read = new() { Name = "tasks.list", Parameters = new JsonObject() };
    private static readonly AiToolDefinitionDto Write = new() { Name = "tasks.create", Parameters = new JsonObject(), RequiresConfirmation = true };

    [Fact]
    public void PromptIncludesBuenosAiresDateTimeUserAndCapabilities()
    {
        // 02:30 UTC del 11/11 todavía es 10/11 en Buenos Aires (UTC-3).
        var utcNow = new DateTime(2026, 11, 11, 2, 30, 0, DateTimeKind.Utc);

        var prompt = AiSystemPromptBuilder.Build(utcNow, AiSystemPromptBuilder.BuenosAiresTimeZone, "Nicolás Luque", [Read, Write]);

        Assert.Contains("(2026-11-10)", prompt);
        Assert.Contains("23:30", prompt);
        Assert.Contains("martes", prompt);
        Assert.Contains("America/Argentina/Buenos_Aires", prompt);
        Assert.Contains("Nicolás Luque", prompt);
        Assert.Contains("Consultas: tasks.list", prompt);
        Assert.Contains("Acciones (requieren confirmación): tasks.create", prompt);
        Assert.Contains("Toda escritura requiere confirmación del usuario", prompt);
        Assert.Contains("Nunca digas que algo se creó", prompt);
        Assert.Contains("rioplatense", prompt);
        Assert.DoesNotContain("únicamente de lectura", prompt);
    }

    [Fact]
    public void WithoutWriteToolsThePromptExplainsItCannotWrite()
    {
        var prompt = AiSystemPromptBuilder.Build(DateTime.UtcNow, AiSystemPromptBuilder.BuenosAiresTimeZone, null, [Read]);

        Assert.Contains("No tenés acciones de escritura habilitadas", prompt);
        Assert.DoesNotContain("Reglas de acciones", prompt);
        Assert.DoesNotContain("Estás hablando con", prompt);
    }
}
