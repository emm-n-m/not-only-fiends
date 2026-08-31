using System.Text.Json;
using System.Text.Json.Serialization;
using NotOnlyFiendsFeed.Contracts;
using NotOnlyFiendsFeed.Services;

var builder = WebApplication.CreateBuilder(args);

// Keep generated assets (the scoped-CSS bundle and Blazor runtime) available when the
// app is run from build output under a non-Development environment. Published builds
// copy these files into wwwroot, but local `dotnet run --no-launch-profile` does not.
builder.WebHost.UseStaticWebAssets();

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
    // Agents must not receive a successful response for a request field the API never reads.
    // TickChoices deliberately has [JsonExtensionData] for per-choice diagnostics; this setting
    // still rejects unknown fields on the request/character/tick objects themselves.
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
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

// Minimal-API model binding rejects a malformed body before any endpoint handler runs, so
// the RunStore/RunEngine wrappers never see it and the client gets a 400 with an empty
// body — nothing to act on. Translate those into the same ErrorResponse shape everything
// else uses. Scoped to /api so Blazor's own request handling is untouched.
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next(context);
        return;
    }

    try
    {
        await next(context);
    }
    catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new ErrorResponse
        {
            Code = "malformed_request",
            // The inner JsonException names the offending property path and value, which is
            // the part a caller actually needs to fix.
            Message = ex.InnerException?.Message ?? ex.Message
        });
    }
});

// JsonUnmappedMemberHandling.Disallow is enforced by Minimal API binding before the endpoint
// delegate runs. In that path ASP.NET Core writes a bare 400, so add the same actionable error
// envelope used by the endpoint wrappers when the framework rejected an API body without one.
app.UseStatusCodePages(async statusContext =>
{
    var context = statusContext.HttpContext;
    if (context.Request.Path.StartsWithSegments("/api")
        && context.Response.StatusCode == StatusCodes.Status400BadRequest)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new ErrorResponse
        {
            Code = "malformed_request",
            Message = "Request JSON contains an unknown field or has an invalid shape."
        });
    }
});

app.MapOpenApi();
app.MapStaticAssets();

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

app.MapGet("/api/content/deities", (AgentApiService api) => TypedResults.Ok(api.GetDeities()));
app.MapGet("/api/content/deities/{id}", (string id, AgentApiService api) => Lookup(() => api.GetDeity(id)));

app.MapGet("/api/content/salient-divine-abilities", (AgentApiService api) => TypedResults.Ok(api.GetSalientDivineAbilities()));
app.MapGet("/api/content/salient-divine-abilities/{id}", (string id, AgentApiService api) => Lookup(() => api.GetSalientDivineAbility(id)));

app.MapGet("/api/content/domains", (AgentApiService api) => TypedResults.Ok(api.GetDomains()));
app.MapGet("/api/content/domains/{id}", (string id, AgentApiService api) => Lookup(() => api.GetDomain(id)));

app.MapGet("/api/content/languages", (AgentApiService api, string? q) => TypedResults.Ok(api.GetLanguages(q)));
app.MapGet("/api/content/skills", (AgentApiService api, string? q) => TypedResults.Ok(api.GetSkills(q)));
app.MapGet("/api/content/skills/{id}", (string id, AgentApiService api) => Lookup(() => api.GetSkill(id)));

app.MapGet("/api/content/class-features", (AgentApiService api) => TypedResults.Ok(api.GetClassFeatures()));
app.MapGet("/api/content/class-features/{id}", (string id, AgentApiService api) => Lookup(() => api.GetClassFeature(id)));

app.MapGet("/api/content/spells", (AgentApiService api, string? listId, int? maxSpellLevel, string? q) =>
    TypedResults.Ok(api.GetSpells(listId, maxSpellLevel, q)));
app.MapGet("/api/content/spells/{id}", (string id, AgentApiService api) => Lookup(() => api.GetSpell(id)));

app.MapGet("/api/content/equipment", (AgentApiService api, NotOnlyFiendsStudio.Models.EquipmentCategory? category, string? q) =>
    TypedResults.Ok(api.GetEquipment(category, q)));
app.MapGet("/api/content/equipment/{id}", (string id, AgentApiService api) => Lookup(() => api.GetEquipmentById(id)));

app.MapPost("/api/characters/evaluate", (EvaluateCharacterRequest request, AgentApiService api) =>
    RunEngine(() => api.Evaluate(request)));

app.MapPost("/api/characters/next-step", (NextStepRequest request, string? optionDetail, AgentApiService api) =>
    RunEngine(() => api.GetNextStep(request, ParseOptionDetail(optionDetail, OptionDetail.None))));

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

app.MapPost("/api/characters", (NotOnlyFiendsStudio.Models.Character character, CharacterStore store, AgentApiService api) =>
    RunMutation(() =>
    {
        api.EvaluateAndEnvelope(string.Empty, character);
        var id = store.Create(character);
        return Results.Created($"/api/characters/{id}", new CharacterEnvelopeDto { Id = id, Character = character });
    }));

app.MapPut("/api/characters/{id}", (string id, NotOnlyFiendsStudio.Models.Character character, AgentApiService api) =>
    RunMutation(() => Results.Ok(api.ReplaceCharacter(id, character))));

app.MapDelete("/api/characters/{id}", (string id, CharacterStore store) =>
    RunStore(() => store.Delete(id)
        ? Results.NoContent()
        : Results.NotFound(new ErrorResponse { Code = "not_found", Message = $"Character not found: {id}" })));

app.MapGet("/api/characters/{id}/sheet", (string id, int? atHd, AgentApiService api) =>
    RunMutation(() =>
    {
        var character = api.LoadCharacter(id);
        return Results.Ok(api.EvaluateAndEnvelope(id, character, atHd).Sheet);
    }));

app.MapGet("/api/characters/{id}/state", (string id, int? atHd, AgentApiService api) =>
    RunMutation(() =>
    {
        var character = api.LoadCharacter(id);
        return Results.Ok(api.EvaluateAndEnvelope(id, character, atHd).State);
    }));

// Previews are on by default now that they no longer inline feat option lists.
// Narrow with ?driverIds=a,b then re-request ?optionDetail=full to get options for
// just the drivers under consideration.
app.MapGet("/api/characters/{id}/next-step",
    (string id, bool? includePreviews, string? optionDetail, string? driverIds, AgentApiService api) =>
        RunMutation(() => Results.Ok(api.GetNextStepById(
            id,
            includePreviews ?? true,
            ParseOptionDetail(optionDetail, OptionDetail.None),
            ParseCsv(driverIds)))));

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

app.MapPost("/api/characters/export-pcg", (ExportPcgRequest request, AgentApiService api) =>
    RunEngine(() => Results.Ok(api.ExportPcg(request.Character, request.Options))));

app.MapGet("/api/characters/{id}/export-pcg", (string id, AgentApiService api) =>
    RunStore(() => Results.Ok(api.ExportPcgById(id))));

// Razor components (catch-all for Blazor pages)
app.MapRazorComponents<NotOnlyFiendsFeed.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();

static OptionDetail ParseOptionDetail(string? value, OptionDetail fallback)
{
    if (string.IsNullOrWhiteSpace(value))
        return fallback;

    return Enum.TryParse<OptionDetail>(value, ignoreCase: true, out var parsed)
        ? parsed
        : throw new ArgumentException(
            $"Unknown optionDetail '{value}'. Expected one of: none, ids, full.");
}

static List<string>? ParseCsv(string? value) =>
    string.IsNullOrWhiteSpace(value)
        ? null
        : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

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
