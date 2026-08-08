using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;
using OpenAI; // Mandatory for OpenAIClientOptions usage

var builder = WebApplication.CreateBuilder(args);

// 1. Setup Postgres (Neon Tech Compatible String Layout Converter)
var connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"] 
                    ?? builder.Configuration["DefaultConnection"]
                    ?? Environment.GetEnvironmentVariable("DefaultConnection");

builder.Services.AddDbContext<KnowledgeContext>(options =>
    options.UseNpgsql(connectionString));

// 2. Setup Groq Proxy Redirects Using Native Client Options Extensions
var groqOptions = new OpenAIClientOptions
{
    Endpoint = new Uri("https://groq.com")
};

builder.Services.AddKernel().AddOpenAIChatCompletion(
    modelId: "llama3-8b-8192", 
    apiKey: builder.Configuration["GroqApiKey"] ?? "YOUR_GROQ_API_KEY",
    options: groqOptions
);

// 3. Register standard retrieval services for our HTTP endpoint injections
builder.Services.AddTransient<IChatCompletionService>(sp => 
    sp.GetRequiredService<Kernel>().GetRequiredService<IChatCompletionService>());

// Enable global permissive CORS parameters for frontend widget integrations
builder.Services.AddCors();

var app = builder.Build();

app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

// Root endpoint to resolve Render health check alerts
app.MapGet("/", () => Results.Text("🤖 Chatbot API Backend is running live!"));

// ENDPOINT A: Dynamic Database Updates
app.MapPost("/api/knowledge/update", async (KnowledgeItem item, KnowledgeContext db) =>
{
    var existing = await db.KnowledgeItems
        .FirstOrDefaultAsync(x => x.Category == item.Category && x.TopicName == item.TopicName);

    if (existing != null)
    {
        existing.ContentDetails = item.ContentDetails;
        existing.LastUpdated = DateTime.UtcNow;
    }
    else
    {
        db.KnowledgeItems.Add(item);
    }
    
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Knowledge base successfully updated!" });
});

// ENDPOINT B: Dynamic RAG Conversational Engine
app.MapPost("/api/chat", async (ChatRequest request, KnowledgeContext db, IChatCompletionService chatService) =>
{
    try
    {
        var currentRecords = await db.KnowledgeItems.ToListAsync();
        var contextBuilder = new StringBuilder();
        foreach (var record in currentRecords)
        {
            contextBuilder.AppendLine($"[{record.Category}] {record.TopicName}: {record.ContentDetails}");
        }

        var systemInstruction = 
            "You are an automated support bot. Answer questions accurately using ONLY the context provided below.\n" +
            "If the answer cannot be verified using the context data, reply with: " +
            "'I do not have that specific detail on file. Let me connect you with a team representative.'\n" +
            "Do not make up facts under any circumstances.\n\n" +
            $"[LIVE NEON DB CONTEXT]\n{contextBuilder}";

        var history = new ChatHistory(systemInstruction);
        history.AddUserMessage(request.UserQuery);

        var response = await chatService.GetChatMessageContentAsync(history);
        return Results.Ok(new { botResponse = response.Content });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[AI LOG CRASH ALERT]: {ex.Message}");
        return Results.Problem($"Chatbot engine processing failed: {ex.Message}");
    }
});

app.Run();

public record ChatRequest(string UserQuery);
