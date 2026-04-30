using System.Text.Json;
using System.Text.Json.Serialization;
using NotOnlyFiendsFeed.Contracts;
using NotOnlyFiendsFeed.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 512 * 1024; // 512KB for file uploads via SignalR
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = true;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

builder.Services.AddSingleton<ServerContentService>();
builder.Services.AddSingleton<CharacterStore>();
builder.Services.AddSingleton<AgentApiService>();
builder.Services.AddScoped<BrowserFileService>();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapOpenApi();

// API endpoints
app.MapGet("/api/health", (AgentApiService api) => TypedResults.Ok(api.GetHealth()));
app.MapGet("/api/rules", (AgentApiService api) => TypedResults.Ok(api.GetRules()));
app.MapGet("/api/content/catalog", (AgentApiService api) => TypedResults.Ok(api.GetCatalog()));

app.MapGet("/api/content/races", (AgentApiService api) => TypedResults.Ok(api.GetRaces()));
app.MapGet("/api/content/races/{id}", (string id, AgentApiService api) => Lookup(() => api.GetRace(id)));

app.MapGet("/api/content/drivers", (AgentApiService api) => TypedResults.Ok(api.GetDrivers()));
app.MapGet("/api/content/drivers/{id}", (string id, AgentApiService api) => Lookup(() => api.GetDriver(id)));

app.MapGet("/api/content/templates", (AgentApiService api) => TypedResults.Ok(api.GetTemplates()));
app.MapGet("/api/content/templates/{id}", (string id, AgentApiService api) => Lookup(() => api.GetTemplate(id)));

app.MapGet("/api/content/feats", (AgentApiService api) => TypedResults.Ok(api.GetFeats()));
app.MapGet("/api/content/feats/{id}", (string id, AgentApiService api) => Lookup(() => api.GetFeat(id)));

app.MapGet("/api/content/domains", (AgentApiService api) => TypedResults.Ok(api.GetDomains()));
app.MapGet("/api/content/domains/{id}", (string id, AgentApiService api) => Lookup(() => api.GetDomain(id)));

app.MapGet("/api/content/skills", (AgentApiService api) => TypedResults.Ok(api.GetSkills()));
app.MapGet("/api/content/skills/{id}", (string id, AgentApiService api) => Lookup(() => api.GetSkill(id)));

app.MapGet("/api/content/class-features", (AgentApiService api) => TypedResults.Ok(api.GetClassFeatures()));
app.MapGet("/api/content/class-features/{id}", (string id, AgentApiService api) => Lookup(() => api.GetClassFeature(id)));

app.MapGet("/api/content/spells", (AgentApiService api, string? listId, int? maxSpellLevel, string? q) =>
    TypedResults.Ok(api.GetSpells(listId, maxSpellLevel, q)));
app.MapGet("/api/content/spells/{id}", (string id, AgentApiService api) => Lookup(() => api.GetSpell(id)));

app.MapPost("/api/characters/evaluate", (EvaluateCharacterRequest request, AgentApiService api) =>
    RunEngine(() => api.Evaluate(request)));

app.MapPost("/api/characters/next-step", (NextStepRequest request, AgentApiService api) =>
    RunEngine(() => api.GetNextStep(request)));

app.MapGet("/api/characters", (CharacterStore store) =>
    RunStore(() =>
    {
        var summaries = store.List()
            .OrderBy(info => info.Name)
            .Select(info => new CharacterSummaryDto
            {
                Id = info.Id,
                Name = info.Name,
                ModifiedUtc = info.ModifiedUtc,
                TotalHd = info.Sheet?.TotalHD,
                Ecl = info.Sheet?.ECL,
                Race = info.Sheet?.Race,
                ClassLevels = info.Sheet?.ClassLevels
            })
            .ToList();
        return Results.Ok(summaries);
    }));

app.MapGet("/api/characters/{id}", (string id, CharacterStore store) =>
    RunStore(() => Results.Ok(new CharacterEnvelopeDto { Id = id, Character = store.Get(id) })));

app.MapPost("/api/characters", (NotOnlyFiendsStudio.Models.Character character, CharacterStore store) =>
    RunStore(() =>
    {
        var id = store.Create(character);
        return Results.Created($"/api/characters/{id}", new CharacterEnvelopeDto { Id = id, Character = character });
    }));

app.MapPut("/api/characters/{id}", (string id, NotOnlyFiendsStudio.Models.Character character, CharacterStore store) =>
    RunStore(() =>
    {
        store.Replace(id, character);
        return Results.Ok(new CharacterEnvelopeDto { Id = id, Character = character });
    }));

app.MapDelete("/api/characters/{id}", (string id, CharacterStore store) =>
    RunStore(() => store.Delete(id)
        ? Results.NoContent()
        : Results.NotFound(new ErrorResponse { Code = "not_found", Message = $"Character not found: {id}" })));

app.MapGet("/api/characters/{id}/sheet", (string id, AgentApiService api) =>
    RunMutation(() =>
    {
        var character = api.LoadCharacter(id);
        return Results.Ok(api.EvaluateAndEnvelope(id, character).Sheet);
    }));

app.MapGet("/api/characters/{id}/state", (string id, AgentApiService api) =>
    RunMutation(() =>
    {
        var character = api.LoadCharacter(id);
        return Results.Ok(api.EvaluateAndEnvelope(id, character).State);
    }));

app.MapGet("/api/characters/{id}/next-step", (string id, bool? includePreviews, AgentApiService api) =>
    RunMutation(() => Results.Ok(api.GetNextStepById(id, includePreviews ?? false))));

app.MapPost("/api/characters/{id}/ticks", (string id, NotOnlyFiendsStudio.Models.Tick tick, AgentApiService api) =>
    RunMutation(() => Results.Ok(api.AppendTick(id, tick))));

app.MapPost("/api/characters/{id}/level-up", (string id, NotOnlyFiendsStudio.Models.Tick tick, AgentApiService api) =>
    RunMutation(() => Results.Ok(api.AppendTick(id, tick))));

app.MapPatch("/api/characters/{id}/ticks/{index:int}", (string id, int index, NotOnlyFiendsStudio.Models.Tick tick, AgentApiService api) =>
    RunMutation(() => Results.Ok(api.UpdateTick(id, index, tick))));

app.MapDelete("/api/characters/{id}/ticks/last", (string id, AgentApiService api) =>
    RunMutation(() => Results.Ok(api.DeleteLastTick(id))));

app.MapPost("/api/characters/{id}/events", (string id, NotOnlyFiendsStudio.Models.PermanentEvent evt, AgentApiService api) =>
    RunMutation(() => Results.Ok(api.AppendEvent(id, evt))));

app.MapPost("/api/characters/{id}/simulate", (string id, NotOnlyFiendsStudio.Models.Tick tick, AgentApiService api) =>
    RunMutation(() => Results.Ok(api.SimulateTick(id, tick))));

app.MapPost("/api/characters/validate", (NotOnlyFiendsStudio.Models.Character character, AgentApiService api) =>
    RunMutation(() => Results.Ok(api.ValidateCharacter(character))));

app.MapPost("/api/characters/import-pcg", (ImportPcgRequest request, bool? save, AgentApiService api) =>
    RunMutation(() =>
    {
        var response = api.ImportPcg(request, save ?? false);
        return (save ?? false) && response.Id != null
            ? Results.Created($"/api/characters/{response.Id}", response)
            : Results.Ok(response);
    }));

// Razor components (catch-all for Blazor pages)
app.MapRazorComponents<NotOnlyFiendsFeed.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();

static IResult Lookup<T>(Func<T> fetch)
{
    try
    {
        return Results.Ok(fetch());
    }
    catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
    {
        return Results.NotFound(new ErrorResponse { Code = "not_found", Message = ex.Message });
    }
}

static IResult RunEngine<T>(Func<T> action)
{
    try
    {
        return Results.Ok(action());
    }
    catch (KeyNotFoundException ex)
    {
        return Results.BadRequest(new ErrorResponse { Code = "not_found", Message = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new ErrorResponse { Code = "invalid_argument", Message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new ErrorResponse { Code = "validation_failed", Message = ex.Message });
    }
}

static IResult RunStore(Func<IResult> action)
{
    try
    {
        return action();
    }
    catch (CharacterStoreException ex)
    {
        return StoreResult(ex);
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new ErrorResponse { Code = "validation_failed", Message = ex.Message });
    }
}

static IResult RunMutation(Func<IResult> action)
{
    try
    {
        return action();
    }
    catch (CharacterStoreException ex)
    {
        return StoreResult(ex);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new ErrorResponse { Code = "not_found", Message = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new ErrorResponse { Code = "invalid_argument", Message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new ErrorResponse { Code = "validation_failed", Message = ex.Message });
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new ErrorResponse { Code = "validation_failed", Message = ex.Message });
    }
}

static IResult StoreResult(CharacterStoreException ex)
{
    var response = new ErrorResponse { Code = ex.Code, Message = ex.Message };
    return ex.Code switch
    {
        "not_configured" => Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable),
        "not_found" => Results.NotFound(response),
        "already_exists" => Results.Conflict(response),
        _ => Results.BadRequest(response)
    };
}
