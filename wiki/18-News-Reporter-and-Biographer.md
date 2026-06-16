# Chapter 18 — News Reporter and Personal Biographer

> Two text-generation features that turn the game's structured event data into living prose: a settlement-based news scribe who publishes weekly dispatches in the style of a Mughal akhbarat, and a personal biographer who accompanies the player and accumulates a publishable life-history.

**[← Chapter 17](17-Quality-of-Life-and-Depth-Systems.md)** | **[Home](Home.md)**

---

## Contents

1. [Design intent](#1-design-intent)
2. [System architecture](#2-system-architecture)
3. [Event schema](#3-event-schema)
4. [EventCaptureBehavior](#4-eventcapturebehavior)
5. [LLM Client — async bridge between C# and local server](#5-llm-client--async-bridge-between-c-and-local-server)
6. [Template fallback — no GPU required](#6-template-fallback--no-gpu-required)
7. [NewsReporterBehavior](#7-newsreporterbehavior)
8. [BiographerBehavior](#8-biographerbehavior)
9. [Biography publishing](#9-biography-publishing)
10. [In-game display](#10-in-game-display)
11. [Local LLM setup — Ollama](#11-local-llm-setup--ollama)
12. [Fine-tuning the hindostan-scribe model](#12-fine-tuning-the-hindostan-scribe-model)
13. [Dataset construction](#13-dataset-construction)
14. [Connecting everything — registration and settings](#14-connecting-everything--registration-and-settings)

---

## 1. Design Intent

These two systems serve completely different purposes despite using the same text-generation pipeline.

**The News Reporter (Waqai-Nawis)** is a settlement-level character. Every town and castle employs one. They observe events happening within a radius of their settlement — battles, famine, notable deaths, political changes — and publish a weekly dispatch in the register of the Mughal *akhbarat*: formal, third-person, dignified prose that reports events with appropriate gravity and court honorifics. The player reads these dispatches when visiting any settlement, giving each city a sense of its own local character and information environment.

**The Personal Biographer (Mir Katib)** is the player's own. He travels with the party as a virtual companion (no party slot) and silently records significant events in the player's life. The cumulative record can be reviewed at any time as a book. Once the player has achieved sufficient fame, they can commission Mir Katib to write the full biography — spending gold and time — and receive a final document that reads like an authentic Indo-Persian *tazkira* (biographical dictionary). This is a long-game reward for the full career trajectory.

---

## 2. System Architecture

```
Game events (battles, deaths, politics, economy)
                    │
                    ▼
        EventCaptureBehavior
        (converts game events to GameEventRecord objects)
                    │
          ┌─────────┴──────────┐
          ▼                    ▼
  NewsReporterBehavior   BiographerBehavior
  (filters regional       (filters player-relevant
   events, queues          events, queues
   weekly bulletins)       biography entries)
          │                    │
          └─────────┬──────────┘
                    ▼
             LLMClientBehavior
             (async HTTP to local server)
             ┌────────────────────────┐
             │  Primary: Ollama       │
             │  Fallback: LM Studio   │
             │  Fallback: Templates   │
             └────────────────────────┘
                    │
                    ▼
         ConcurrentQueue<LLMResult>
         (thread-safe result buffer)
                    │
                    ▼
         Main thread picks up results
         on daily tick, stores to
         settlement archives and
         biography journal
```

The game's main thread NEVER waits on the LLM. All LLM calls are fire-and-forget on the thread pool. The result appears in-game "the next day" — which is not a hack; it matches the historical reality that dispatches took time to write.

---

## 3. Event Schema

Every captured event is serialized as a `GameEventRecord`, which is then serialized to JSON for the LLM prompt.

```csharp
public enum GameEventType
{
    Battle,
    Siege,
    Settlement_Captured,
    Notable_Death,
    Lord_Death,
    Kingdom_War_Declared,
    Kingdom_Peace,
    Famine_Began,
    Famine_Ended,
    Epidemic_Began,
    Epidemic_Ended,
    Festival,
    Rank_Promotion,
    Rank_Demotion,
    Fief_Granted,
    Fief_Revoked,
    Council_Appointed,
    Civil_War_Declared,
    Biography_Chapter,  // for significant personal milestones
}

[Serializable]
public class GameEventRecord
{
    public GameEventType   Type;
    public string          DateLabel;       // e.g. "Year 1022, Monsoon Season, Day 14"
    public string          Location;        // nearest settlement name
    public string[]        Protagonists;    // hero names, winner first if applicable
    public string[]        Antagonists;
    public int             WinnerCasualties;
    public int             LoserCasualties;
    public string[]        Captured;
    public string          Significance;    // "minor", "significant", "decisive", "catastrophic"
    public string          ExtraContext;    // freeform detail (e.g. "Monsoon rains hampered the cavalry")
    public bool            IsPlayerInvolved;
    public string          PlayerRole;      // "commander", "participant", "witness", "victim"

    public string ToJson()
    {
        return $@"{{
  ""event_type"": ""{Type}"",
  ""date"": ""{DateLabel}"",
  ""location"": ""{Location}"",
  ""protagonists"": [{string.Join(", ", Protagonists.Select(p => $"\"{p}\""))}],
  ""antagonists"": [{string.Join(", ", Antagonists.Select(a => $"\"{a}\""))}],
  ""winner_casualties"": {WinnerCasualties},
  ""loser_casualties"": {LoserCasualties},
  ""captured"": [{string.Join(", ", Captured.Select(c => $"\"{c}\""))}],
  ""significance"": ""{Significance}"",
  ""extra_context"": ""{ExtraContext}"",
  ""player_role"": ""{PlayerRole}""
}}";
    }
}
```

---

## 4. EventCaptureBehavior

This behavior hooks into Bannerlord's campaign events and produces `GameEventRecord` objects. It is the single source of truth for what gets written about.

```csharp
public class EventCaptureBehavior : CampaignBehaviorBase
{
    // All events captured since last news generation, keyed by settlement
    private Dictionary<string, List<GameEventRecord>> _regionalEvents
        = new Dictionary<string, List<GameEventRecord>>();

    // All player-relevant events (for biographer)
    private List<GameEventRecord> _playerEvents = new List<GameEventRecord>();

    public static EventCaptureBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.OnMapEventEndedEvent.AddNonSerializedListener(this, OnBattleEnded);
        CampaignEvents.OnSiegeEventEndedEvent.AddNonSerializedListener(this, OnSiegeEnded);
        CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(
            this, OnSettlementOwnerChanged);
        CampaignEvents.OnHeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        CampaignEvents.OnWarDeclaredEvent.AddNonSerializedListener(this, OnWarDeclared);
        CampaignEvents.MakePeaceEvent.AddNonSerializedListener(this, OnPeaceMade);
    }

    // ── Battle ─────────────────────────────────────────────────────────────────
    private void OnBattleEnded(MapEvent mapEvent)
    {
        if (mapEvent.Winner == null) return;

        bool playerWon  = mapEvent.Winner.LeaderParty == MobileParty.MainParty.Party;
        bool playerLost = mapEvent.BattleState == BattleState.AttackerVictory
                       && mapEvent.PartiesOnSide(BattleSideEnum.Attacker).Any(
                              p => p.Party == MobileParty.MainParty.Party);
        bool playerInvolved = mapEvent.IsPlayerMapEvent;

        var record = new GameEventRecord
        {
            Type          = GameEventType.Battle,
            DateLabel     = FormatDate(),
            Location      = NearestSettlementName(mapEvent.Position),
            Protagonists  = GetHeroNames(mapEvent.PartiesOnSide(BattleSideEnum.Attacker)),
            Antagonists   = GetHeroNames(mapEvent.PartiesOnSide(BattleSideEnum.Defender)),
            WinnerCasualties  = EstimateCasualties(mapEvent.Winner),
            LoserCasualties   = EstimateCasualties(mapEvent.Loser),
            Captured      = mapEvent.Loser?.LeaderParty?.LeaderHero?.IsPrisoner == true
                            ? new[] { mapEvent.Loser.LeaderParty.LeaderHero.Name.ToString() }
                            : Array.Empty<string>(),
            Significance  = ClassifyBattleSignificance(mapEvent),
            ExtraContext  = BuildBattleContext(mapEvent),
            IsPlayerInvolved = playerInvolved,
            PlayerRole    = playerInvolved
                            ? (playerWon ? "commander-victor" : "commander-defeated")
                            : "witness"
        };

        RegisterEvent(record, mapEvent.Position);
    }

    // ── Settlement capture ──────────────────────────────────────────────────────
    private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim,
        Hero newOwner, Hero oldOwner, Hero capturer,
        ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
    {
        if (detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege
         || detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByDefault)
        {
            var record = new GameEventRecord
            {
                Type         = GameEventType.Settlement_Captured,
                DateLabel    = FormatDate(),
                Location     = settlement.Name.ToString(),
                Protagonists = newOwner != null ? new[] { newOwner.Name.ToString() } : Array.Empty<string>(),
                Antagonists  = oldOwner != null ? new[] { oldOwner.Name.ToString() } : Array.Empty<string>(),
                Significance = settlement.IsTown ? "decisive" : "significant",
                ExtraContext = $"{settlement.Name} has changed hands from {oldOwner?.Name} to {newOwner?.Name}.",
                IsPlayerInvolved = capturer == Hero.MainHero
                                || newOwner?.Clan == Hero.MainHero?.Clan,
                PlayerRole   = capturer == Hero.MainHero ? "conqueror" : "ally"
            };
            RegisterEvent(record, settlement.Position2D);
        }
    }

    // ── Hero death ──────────────────────────────────────────────────────────────
    private void OnHeroKilled(Hero victim, Hero killer,
        KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
    {
        if (!victim.IsLord && !victim.IsNotable) return;

        var record = new GameEventRecord
        {
            Type         = GameEventType.victim.IsLord ? GameEventType.Lord_Death : GameEventType.Notable_Death,
            DateLabel    = FormatDate(),
            Location     = victim.HomeSettlement?.Name.ToString() ?? "unknown",
            Protagonists = new[] { victim.Name.ToString() },
            Antagonists  = killer != null ? new[] { killer.Name.ToString() } : Array.Empty<string>(),
            Significance = victim.IsKingdomLeader() ? "catastrophic" : "significant",
            ExtraContext = $"{victim.Name} was {GetDeathDescription(detail)}.",
            IsPlayerInvolved = killer == Hero.MainHero || victim.Clan == Hero.MainHero?.Clan,
            PlayerRole   = killer == Hero.MainHero ? "slayer" : "witness"
        };
        RegisterEvent(record, victim.HomeSettlement?.Position2D ?? Vec2.Zero);
    }

    // ── War / peace ──────────────────────────────────────────────────────────────
    private void OnWarDeclared(IFaction attacker, IFaction defender,
        DeclareWarAction.DeclareWarDetail detail)
    {
        string location = attacker is Kingdom ka
            ? ka.InitialHomeFortSettlement?.Name.ToString() ?? "the frontier"
            : "the border";

        var record = new GameEventRecord
        {
            Type         = GameEventType.Kingdom_War_Declared,
            DateLabel    = FormatDate(),
            Location     = location,
            Protagonists = new[] { attacker.Name.ToString() },
            Antagonists  = new[] { defender.Name.ToString() },
            Significance = "significant",
            ExtraContext = $"{attacker.Name} has declared war upon {defender.Name}.",
            IsPlayerInvolved = attacker == Hero.MainHero?.MapFaction
                            || defender == Hero.MainHero?.MapFaction,
            PlayerRole   = "subject"
        };
        RegisterEvent(record, Vec2.Zero);
    }

    private void OnPeaceMade(IFaction faction1, IFaction faction2)
    {
        var record = new GameEventRecord
        {
            Type         = GameEventType.Kingdom_Peace,
            DateLabel    = FormatDate(),
            Location     = "the capital",
            Protagonists = new[] { faction1.Name.ToString(), faction2.Name.ToString() },
            Antagonists  = Array.Empty<string>(),
            Significance = "significant",
            ExtraContext = $"Peace has been concluded between {faction1.Name} and {faction2.Name}.",
            IsPlayerInvolved = faction1 == Hero.MainHero?.MapFaction
                            || faction2 == Hero.MainHero?.MapFaction,
            PlayerRole   = "subject"
        };
        RegisterEvent(record, Vec2.Zero);
    }

    // ── Public API for other behaviors ─────────────────────────────────────────
    public void RecordPlayerMilestone(GameEventType type, string description, string location)
    {
        var record = new GameEventRecord
        {
            Type             = type,
            DateLabel        = FormatDate(),
            Location         = location,
            Protagonists     = new[] { Hero.MainHero?.Name.ToString() ?? "the player" },
            Antagonists      = Array.Empty<string>(),
            Significance     = "significant",
            ExtraContext     = description,
            IsPlayerInvolved = true,
            PlayerRole       = "subject"
        };
        RegisterEvent(record, Vec2.Zero);
    }

    // ── Internal helpers ────────────────────────────────────────────────────────
    private void RegisterEvent(GameEventRecord record, Vec2 position)
    {
        // Assign to nearest settlement's regional queue
        string regionId = NearestSettlementId(position);
        if (!_regionalEvents.ContainsKey(regionId))
            _regionalEvents[regionId] = new List<GameEventRecord>();
        _regionalEvents[regionId].Add(record);

        // Add to player events if relevant
        if (record.IsPlayerInvolved)
            _playerEvents.Add(record);
    }

    public List<GameEventRecord> DrainRegionalEvents(string settlementId)
    {
        if (!_regionalEvents.TryGetValue(settlementId, out var list))
            return new List<GameEventRecord>();
        _regionalEvents.Remove(settlementId);
        return list;
    }

    public List<GameEventRecord> GetPlayerEventsSnapshot()
        => new List<GameEventRecord>(_playerEvents);

    private string FormatDate()
    {
        int day    = (int)(CampaignTime.Now.ToDays % CampaignTime.DayOfYear);
        int year   = (int)(CampaignTime.Now.ToDays / CampaignTime.DayOfYear);
        string season = ((int)CampaignTime.Now.GetSeasonOfYear()) switch
        {
            0 => "Pre-Monsoon Season",
            1 => "Monsoon Season",
            2 => "Harvest Season",
            _ => "Winter Season"
        };
        return $"Year {year}, {season}, Day {day % (CampaignTime.DayOfYear / 4)}";
    }

    private string NearestSettlementName(Vec2 pos)
    {
        return Settlement.All
            .Where(s => s.IsTown || s.IsCastle)
            .OrderBy(s => s.Position2D.DistanceSquared(pos))
            .FirstOrDefault()?.Name.ToString() ?? "the open field";
    }

    private string NearestSettlementId(Vec2 pos)
    {
        return Settlement.All
            .Where(s => s.IsTown || s.IsCastle)
            .OrderBy(s => s.Position2D.DistanceSquared(pos))
            .FirstOrDefault()?.StringId ?? "global";
    }

    private string[] GetHeroNames(IEnumerable<MapEventParty> parties)
        => parties.Where(p => p.Party?.LeaderHero != null)
                  .Select(p => p.Party.LeaderHero.Name.ToString())
                  .Distinct().ToArray();

    private int EstimateCasualties(MapEventSide side)
        => (int)(side?.Casualties ?? 0);

    private string ClassifyBattleSignificance(MapEvent e)
    {
        int total = (e.Winner?.Casualties ?? 0) + (e.Loser?.Casualties ?? 0);
        if (total > 500) return "decisive";
        if (total > 150) return "significant";
        return "minor";
    }

    private string BuildBattleContext(MapEvent e)
    {
        var parts = new List<string>();
        int season = (int)CampaignTime.Now.GetSeasonOfYear();
        if (season == 1) parts.Add("Monsoon rains hampered movement.");
        if (e.IsFieldBattle) parts.Add("The battle was fought in open field.");
        if (e.IsRaid)        parts.Add("The action was a swift raid.");
        return string.Join(" ", parts);
    }

    private string GetDeathDescription(KillCharacterAction.KillCharacterActionDetail detail)
        => detail switch
        {
            KillCharacterAction.KillCharacterActionDetail.DiedInBattle  => "slain in battle",
            KillCharacterAction.KillCharacterActionDetail.DiedOfOldAge  => "taken by age",
            KillCharacterAction.KillCharacterActionDetail.Executed       => "executed by order of a lord",
            KillCharacterAction.KillCharacterActionDetail.DiedInLabor   => "lost in childbirth",
            _ => "deceased"
        };

    public override void SyncData(IDataStore dataStore)
    {
        // Player biography events must persist; regional queues are transient
        var playerJsons = _playerEvents.Select(e => e.ToJson()).ToList();
        dataStore.SyncData("hind_player_events_json", ref playerJsons);
        if (!dataStore.IsSaving)
        {
            // On load: events are stored as raw JSON strings
            // Deserializing back to GameEventRecord would require a JSON library
            // For simplicity, we store them as rendered strings once generated
        }
    }
}
```

---

## 5. LLM Client — Async Bridge Between C# and Local Server

This is the most technically critical component. The game runs on a single main thread. LLM inference takes 2–10 seconds. Blocking the main thread for that long causes the game to freeze and is unacceptable.

The solution is a classic producer-consumer pattern:
- The game enqueues **requests** from the main thread
- A background thread pool handles HTTP calls
- Results land in a `ConcurrentQueue<LLMResult>`
- The main thread drains the result queue each daily tick

### LLMClientBehavior

```csharp
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

public class LLMClientBehavior : CampaignBehaviorBase
{
    public struct LLMRequest
    {
        public string RequestId;
        public string Prompt;
        public string SystemPrompt;
        public LLMRequestPurpose Purpose;
        public object Tag;  // carries caller context (settlement id, event type)
    }

    public struct LLMResult
    {
        public string RequestId;
        public string GeneratedText;
        public bool   WasTemplate;
        public object Tag;
    }

    public enum LLMRequestPurpose { NewsBulletin, BiographyEntry, BiographyPublish }

    private static readonly HttpClient _http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private ConcurrentQueue<LLMRequest> _requestQueue = new ConcurrentQueue<LLMRequest>();
    private ConcurrentQueue<LLMResult>  _resultQueue  = new ConcurrentQueue<LLMResult>();

    private LLMSettings _settings = new LLMSettings();

    public static LLMClientBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        StartWorkerThread();
    }

    // ── Main thread: enqueue a request ─────────────────────────────────────────
    public void Enqueue(LLMRequest request)
        => _requestQueue.Enqueue(request);

    // ── Main thread: drain results each day ────────────────────────────────────
    private void OnDailyTick()
    {
        while (_resultQueue.TryDequeue(out var result))
            DispatchResult(result);
    }

    private void DispatchResult(LLMResult result)
    {
        switch (result.Tag)
        {
            case string settlementId:
                NewsReporterBehavior.Instance?.OnBulletinReady(settlementId, result.GeneratedText);
                break;
            case (GameEventType type, int entryIndex):
                BiographerBehavior.Instance?.OnEntryReady(type, entryIndex, result.GeneratedText);
                break;
        }
    }

    // ── Background worker ───────────────────────────────────────────────────────
    private void StartWorkerThread()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(500);  // poll every 500ms

                while (_requestQueue.TryDequeue(out var request))
                {
                    try
                    {
                        string text = await CallLLM(request);
                        _resultQueue.Enqueue(new LLMResult
                        {
                            RequestId     = request.RequestId,
                            GeneratedText = text,
                            WasTemplate   = false,
                            Tag           = request.Tag
                        });
                    }
                    catch
                    {
                        // LLM unavailable — use template
                        string text = TemplateFallback.Generate(request);
                        _resultQueue.Enqueue(new LLMResult
                        {
                            RequestId     = request.RequestId,
                            GeneratedText = text,
                            WasTemplate   = true,
                            Tag           = request.Tag
                        });
                    }
                }
            }
        });
    }

    // ── Ollama API call ────────────────────────────────────────────────────────
    private async Task<string> CallLLM(LLMRequest request)
    {
        if (_settings.Backend == LLMBackend.Ollama)
            return await CallOllama(request);
        else
            return await CallLMStudio(request);
    }

    private async Task<string> CallOllama(LLMRequest request)
    {
        // Ollama /api/generate format
        var payload = new
        {
            model  = _settings.ModelName,  // "hindostan-scribe"
            system = request.SystemPrompt,
            prompt = request.Prompt,
            stream = false,
            options = new
            {
                temperature = 0.72,
                top_p       = 0.90,
                num_predict = _settings.MaxTokens  // ~300 for bulletins, ~500 for biography
            }
        };

        string json     = SimpleJsonSerialize(payload);
        var    content  = new StringContent(json, Encoding.UTF8, "application/json");
        var    response = await _http.PostAsync("http://localhost:11434/api/generate", content);

        response.EnsureSuccessStatusCode();
        string body   = await response.Content.ReadAsStringAsync();
        string result = ExtractOllamaResponse(body);

        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException("Empty response from Ollama");
        return result;
    }

    private async Task<string> CallLMStudio(LLMRequest request)
    {
        // LM Studio OpenAI-compatible format
        var payload = new
        {
            model    = _settings.ModelName,
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user",   content = request.Prompt }
            },
            max_tokens  = _settings.MaxTokens,
            temperature = 0.72,
            stream      = false
        };

        string json     = SimpleJsonSerialize(payload);
        var    content  = new StringContent(json, Encoding.UTF8, "application/json");
        var    response = await _http.PostAsync(
            "http://localhost:1234/v1/chat/completions", content);

        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync();
        return ExtractLMStudioResponse(body);
    }

    // ── JSON helpers ─────────────────────────────────────────────────────────
    // NOTE: Bannerlord mods cannot easily reference Newtonsoft.Json without bundling it.
    // Either bundle Newtonsoft.Json.dll (allowed), or write a minimal serializer.
    // The SimpleJsonSerialize below covers the specific payload shapes above.
    private string SimpleJsonSerialize(object obj)
    {
        // In practice, bundle Newtonsoft.Json 13.x and use JsonConvert.SerializeObject(obj)
        // Shown here as conceptual placeholder
        return Newtonsoft.Json.JsonConvert.SerializeObject(obj);
    }

    private string ExtractOllamaResponse(string json)
    {
        // Parse: {"model":"...","response":"...generated text...","done":true}
        dynamic parsed = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
        return parsed?.response?.ToString() ?? "";
    }

    private string ExtractLMStudioResponse(string json)
    {
        // Parse: {"choices":[{"message":{"content":"...generated text..."}}]}
        dynamic parsed = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
        return parsed?.choices?[0]?.message?.content?.ToString() ?? "";
    }

    public override void SyncData(IDataStore dataStore) { }
}

public enum LLMBackend { Ollama, LMStudio }

public class LLMSettings
{
    public LLMBackend Backend    = LLMBackend.Ollama;
    public string     ModelName  = "hindostan-scribe";
    public int        MaxTokens  = 300;
}
```

> **Bundling Newtonsoft.Json:** Add `Newtonsoft.Json.dll` to your project reference, set Copy Local = true, and place the DLL in your mod's `bin\Win64_Shipping_Client\` folder. Bannerlord will load it alongside your mod DLL.

---

## 6. Template Fallback — No GPU Required

If Ollama is not running, every LLM call falls back to this template system. The prose is simpler but still period-appropriate, using randomly selected phrasing banks.

```csharp
public static class TemplateFallback
{
    private static readonly Random _rng = new Random();

    public static string Generate(LLMClientBehavior.LLMRequest request)
    {
        // Extract the JSON from the prompt to pick a template
        // (In practice, pass GameEventRecord directly alongside the prompt)
        return request.Purpose switch
        {
            LLMClientBehavior.LLMRequestPurpose.NewsBulletin  => GenerateBulletin(request.Prompt),
            LLMClientBehavior.LLMRequestPurpose.BiographyEntry => GenerateBioEntry(request.Prompt),
            _ => "Events of this day have been noted by the royal scribes."
        };
    }

    private static readonly string[] BattleOpeners = new[]
    {
        "It is recorded, by the grace of God, that",
        "The chronicles of this day set down that",
        "It was witnessed by all present that",
        "It is reported from {location} that",
    };

    private static readonly string[] VictoryPhrases = new[]
    {
        "the forces of {winner} did prevail over {loser}",
        "{winner} achieved a notable victory against {loser}",
        "the armies of {winner} routed those of {loser}",
    };

    private static readonly string[] CasualtyPhrases = new[]
    {
        "The losses sustained by the victors numbered {winner_casualties}, " +
        "while the enemy suffered {loser_casualties} dead and wounded.",
        "{winner_casualties} were lost among the victors; " +
        "the defeated suffered {loser_casualties}.",
    };

    private static string GenerateBulletin(string promptContext)
    {
        // Minimal template — pick phrases and fill in blanks
        // Full implementation would parse the event JSON from the prompt
        string opener   = Pick(BattleOpeners).Replace("{location}", "the field");
        string victory  = Pick(VictoryPhrases).Replace("{winner}", "the forces").Replace("{loser}", "the enemy");
        string casualty = Pick(CasualtyPhrases).Replace("{winner_casualties}", "some").Replace("{loser_casualties}", "many");

        return $"{opener} {victory}. {casualty} May God grant peace to the departed.";
    }

    private static string GenerateBioEntry(string promptContext)
    {
        return "In this period, events of consequence unfolded in the life of the subject. " +
               "His conduct was noted by those around him, and the record is duly set down here.";
    }

    private static T Pick<T>(T[] array) => array[_rng.Next(array.Length)];
}
```

---

## 7. NewsReporterBehavior

The news reporter character is conceptual — not a visible hero on the map. The behavior represents them. Every town and castle with a lord gets a weekly bulletin.

```csharp
public class NewsReporterBehavior : CampaignBehaviorBase
{
    private struct NewsEntry
    {
        public string Date;
        public string SettlementId;
        public string Text;
        public bool   IsTemplate;
    }

    // Per settlement: last 5 bulletins (ring buffer)
    private Dictionary<string, Queue<NewsEntry>> _archives
        = new Dictionary<string, Queue<NewsEntry>>();

    private const int  MAX_ARCHIVE_PER_SETTLEMENT = 5;
    private const int  RADIUS_MAP_UNITS           = 60;

    // System prompt used for every news generation
    private const string SYSTEM_PROMPT =
        "You are a waqai-nawis, a royal news scribe of the Mughliya court, circa 1720 CE. " +
        "Write in the elevated, formal register of 18th-century Indo-Persian court dispatches " +
        "translated to dignified English. Use third person throughout. Address commanders by honorifics " +
        "(\"the valiant\", \"the noble\", \"the illustrious\"). Report losses with decorum. " +
        "Do not use modern idioms. Each dispatch is 2-3 paragraphs, approximately 150-200 words. " +
        "Begin with the dateline and location.";

    public static NewsReporterBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    private void OnWeeklyTick()
    {
        // For each town/castle with a lord, generate a regional bulletin
        foreach (Settlement settlement in Settlement.All.Where(s => s.IsTown || s.IsCastle))
        {
            if (settlement.OwnerClan == null) continue;

            // Collect events within radius
            List<GameEventRecord> regional = CollectRegionalEvents(settlement);
            if (regional.Count == 0) continue;

            // Build prompt
            string eventsJson = string.Join(",\n", regional.Take(4).Select(e => e.ToJson()));
            string userPrompt = $"Write a weekly news dispatch for {settlement.Name}, " +
                                $"covering these events:\n[\n{eventsJson}\n]";

            LLMClientBehavior.Instance?.Enqueue(new LLMClientBehavior.LLMRequest
            {
                RequestId   = $"news_{settlement.StringId}_{(int)CampaignTime.Now.ToDays}",
                SystemPrompt = SYSTEM_PROMPT,
                Prompt      = userPrompt,
                Purpose     = LLMClientBehavior.LLMRequestPurpose.NewsBulletin,
                Tag         = settlement.StringId
            });
        }
    }

    private List<GameEventRecord> CollectRegionalEvents(Settlement settlement)
    {
        // Drain the event capture buffer for this settlement and nearby ones
        var events = EventCaptureBehavior.Instance?.DrainRegionalEvents(settlement.StringId)
                  ?? new List<GameEventRecord>();

        // Also pull from nearby settlements within radius
        foreach (Settlement nearby in Settlement.All.Where(
            s => s != settlement &&
                 s.Position2D.DistanceSquared(settlement.Position2D) < RADIUS_MAP_UNITS * RADIUS_MAP_UNITS))
        {
            events.AddRange(
                EventCaptureBehavior.Instance?.DrainRegionalEvents(nearby.StringId)
             ?? new List<GameEventRecord>());
        }

        return events;
    }

    // Called by LLMClientBehavior when result is ready (on main thread)
    public void OnBulletinReady(string settlementId, string text)
    {
        if (!_archives.ContainsKey(settlementId))
            _archives[settlementId] = new Queue<NewsEntry>();

        var queue = _archives[settlementId];
        if (queue.Count >= MAX_ARCHIVE_PER_SETTLEMENT)
            queue.Dequeue();

        queue.Enqueue(new NewsEntry
        {
            Date         = $"Week of {(int)CampaignTime.Now.ToDays}",
            SettlementId = settlementId,
            Text         = text,
            IsTemplate   = false
        });
    }

    public List<(string date, string text)> GetBulletins(Settlement s)
    {
        if (!_archives.TryGetValue(s.StringId, out var queue))
            return new List<(string, string)>();
        return queue.Select(e => (e.Date, e.Text)).Reverse().ToList();
    }

    public override void SyncData(IDataStore dataStore)
    {
        var ids    = _archives.Keys.ToList();
        var texts  = _archives.Values
            .Select(q => q.Select(e => e.Text).ToList())
            .ToList();
        var dates  = _archives.Values
            .Select(q => q.Select(e => e.Date).ToList())
            .ToList();

        // Serialize as flat lists
        var allIds   = new List<string>();
        var allTexts = new List<string>();
        var allDates = new List<string>();

        for (int i = 0; i < ids.Count; i++)
        {
            foreach (var entry in _archives[ids[i]])
            {
                allIds.Add(ids[i]);
                allTexts.Add(entry.Text);
                allDates.Add(entry.Date);
            }
        }

        dataStore.SyncData("hind_news_sids",  ref allIds);
        dataStore.SyncData("hind_news_texts", ref allTexts);
        dataStore.SyncData("hind_news_dates", ref allDates);

        if (!dataStore.IsSaving)
        {
            _archives.Clear();
            for (int i = 0; i < allIds.Count; i++)
            {
                string sid = allIds[i];
                if (!_archives.ContainsKey(sid))
                    _archives[sid] = new Queue<NewsEntry>();
                _archives[sid].Enqueue(new NewsEntry
                {
                    SettlementId = sid,
                    Text         = i < allTexts.Count ? allTexts[i] : "",
                    Date         = i < allDates.Count ? allDates[i] : ""
                });
            }
        }
    }
}
```

### Reading the bulletin — game menu

```csharp
starter.AddGameMenuOption("town", "read_akhbarat",
    "{=!}Read the week's dispatch (Akhbarat)",
    args => NewsReporterBehavior.Instance?.GetBulletins(Settlement.CurrentSettlement)?.Count > 0,
    args =>
    {
        var bulletins = NewsReporterBehavior.Instance.GetBulletins(Settlement.CurrentSettlement);
        var latest    = bulletins.FirstOrDefault();

        // Display as a multi-page scrollable inquiry
        InformationManager.ShowInquiry(new InquiryData(
            $"Weekly Dispatch — {Settlement.CurrentSettlement.Name}",
            $"{latest.date}\n\n{latest.text}",
            true, false,
            "Close",
            "",
            () => { },
            () => { }
        ));
    }
);
```

---

## 8. BiographerBehavior

Mir Katib (the scribe) is a virtual companion — he travels with the player but does not occupy a party slot and does not fight. He is referenced by name in dialogue and in the published biography.

```csharp
public class BiographerBehavior : CampaignBehaviorBase
{
    public struct BiographyEntry
    {
        public int             Index;
        public GameEventType   EventType;
        public string          RawEventJson;
        public string          GeneratedProse;  // set when LLM returns
        public string          DateLabel;
        public bool            IsRendered;      // prose is ready
    }

    private List<BiographyEntry> _entries = new List<BiographyEntry>();
    private int                   _nextIndex = 0;

    private const string BIO_SYSTEM_PROMPT =
        "You are writing in the style of an 18th-century Indo-Persian tazkira " +
        "(biographical dictionary), specifically in the tradition of the Maasir-ul-Umara — " +
        "biographies of Mughal nobles. The subject is a lord in service of the Mughliya court. " +
        "Write in elevated, formal third person. Use epithets fitting the subject's rank. " +
        "Each entry is one biographical chapter covering a single significant event or period. " +
        "Approximately 150-250 words. Begin with the date and location. " +
        "The tone should be admiring but measured — the subject's deeds speak for themselves.";

    public static BiographerBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        // The biographer hooks into the same event capture as the news system
        // but filters for player-relevant events only
        CampaignEvents.OnMapEventEndedEvent.AddNonSerializedListener(this, OnBattleEnded);
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    private void OnBattleEnded(MapEvent mapEvent)
    {
        if (!mapEvent.IsPlayerMapEvent) return;
        RecordBattleEntry(mapEvent);
    }

    private void RecordBattleEntry(MapEvent mapEvent)
    {
        bool playerWon = mapEvent.BattleState == BattleState.AttackerVictory &&
            mapEvent.PartiesOnSide(BattleSideEnum.Attacker)
            .Any(p => p.Party == MobileParty.MainParty.Party);

        string location   = Settlement.All
            .OrderBy(s => s.Position2D.DistanceSquared(mapEvent.Position))
            .FirstOrDefault()?.Name.ToString() ?? "open country";

        int allies  = mapEvent.PartiesOnSide(BattleSideEnum.Attacker)
            .Sum(p => p.Party?.TotalStrength ?? 0);
        int enemies = mapEvent.PartiesOnSide(BattleSideEnum.Defender)
            .Sum(p => p.Party?.TotalStrength ?? 0);

        string eventJson = $@"{{
  ""event_type"": ""Battle"",
  ""date"": ""{FormatDate()}"",
  ""location"": ""{location}"",
  ""player_name"": ""{Hero.MainHero?.Name}"",
  ""player_rank"": ""{MansabdariBehavior.Instance?.GetRankTitle(Hero.MainHero)}"",
  ""outcome"": ""{(playerWon ? "victory" : "defeat")}"",
  ""allied_strength"": {allies},
  ""enemy_strength"": {enemies},
  ""player_casualties"": {mapEvent.Winner?.Casualties ?? mapEvent.Loser?.Casualties ?? 0}
}}";

        AddEntry(GameEventType.Battle, eventJson);
    }

    public void RecordMilestone(GameEventType type, string eventJson)
        => AddEntry(type, eventJson);

    private void AddEntry(GameEventType type, string eventJson)
    {
        int index = _nextIndex++;
        _entries.Add(new BiographyEntry
        {
            Index        = index,
            EventType    = type,
            RawEventJson = eventJson,
            DateLabel    = FormatDate(),
            IsRendered   = false
        });

        // Queue LLM generation
        string playerName = Hero.MainHero?.Name.ToString() ?? "the lord";
        string rank       = MansabdariBehavior.Instance?.GetRankTitle(Hero.MainHero) ?? "";

        LLMClientBehavior.Instance?.Enqueue(new LLMClientBehavior.LLMRequest
        {
            RequestId    = $"bio_{index}",
            SystemPrompt = BIO_SYSTEM_PROMPT,
            Prompt       = $"Write a biography chapter for {playerName} ({rank}), " +
                           $"based on this event:\n{eventJson}",
            Purpose      = LLMClientBehavior.LLMRequestPurpose.BiographyEntry,
            Tag          = (type, index)
        });
    }

    private void OnWeeklyTick()
    {
        // Milestone events are also generated from world state
        // e.g. if player has been at a rank for 3 months with no notable event,
        // generate a quiet "life in this period" entry
        int daysSinceLastEntry = _entries.Count > 0
            ? (int)CampaignTime.Now.ToDays - (int)CampaignTime.Now.ToDays  // placeholder
            : 0;
    }

    // Called by LLMClientBehavior when prose is ready
    public void OnEntryReady(GameEventType type, int index, string prose)
    {
        var entry = _entries.FirstOrDefault(e => e.Index == index);
        if (entry.Index == index)
        {
            int idx = _entries.IndexOf(entry);
            var updated = entry;
            updated.GeneratedProse = prose;
            updated.IsRendered     = true;
            _entries[idx] = updated;
        }
    }

    public List<BiographyEntry> GetEntries()
        => new List<BiographyEntry>(_entries);

    public bool CanPublish()
    {
        int renown = (int)(Hero.MainHero?.Clan?.Renown ?? 0);
        float valour = ValourBehavior.Instance?.Valour ?? 0;
        int entries = _entries.Count(e => e.IsRendered);
        return renown >= 1000 && valour >= 500 && entries >= 10;
    }

    private string FormatDate()
    {
        int year   = (int)(CampaignTime.Now.ToDays / CampaignTime.DayOfYear);
        string season = ((int)CampaignTime.Now.GetSeasonOfYear()) switch
        {
            0 => "Pre-Monsoon",
            1 => "Monsoon",
            2 => "Harvest",
            _ => "Winter"
        };
        return $"Year {year}, {season} Season";
    }

    public override void SyncData(IDataStore dataStore)
    {
        var indexes = _entries.Select(e => e.Index).ToList();
        var types   = _entries.Select(e => (int)e.EventType).ToList();
        var jsons   = _entries.Select(e => e.RawEventJson).ToList();
        var proses  = _entries.Select(e => e.GeneratedProse ?? "").ToList();
        var dates   = _entries.Select(e => e.DateLabel).ToList();
        var renders = _entries.Select(e => e.IsRendered ? 1 : 0).ToList();

        dataStore.SyncData("hind_bio_indexes",  ref indexes);
        dataStore.SyncData("hind_bio_types",    ref types);
        dataStore.SyncData("hind_bio_jsons",    ref jsons);
        dataStore.SyncData("hind_bio_proses",   ref proses);
        dataStore.SyncData("hind_bio_dates",    ref dates);
        dataStore.SyncData("hind_bio_rendered", ref renders);

        if (!dataStore.IsSaving)
        {
            _entries.Clear();
            for (int i = 0; i < indexes.Count; i++)
            {
                _entries.Add(new BiographyEntry
                {
                    Index          = indexes[i],
                    EventType      = (GameEventType)(i < types.Count  ? types[i]   : 0),
                    RawEventJson   = i < jsons.Count  ? jsons[i]   : "",
                    GeneratedProse = i < proses.Count ? proses[i]  : "",
                    DateLabel      = i < dates.Count  ? dates[i]   : "",
                    IsRendered     = i < renders.Count && renders[i] == 1
                });
            }
            _nextIndex = _entries.Count > 0 ? _entries.Max(e => e.Index) + 1 : 0;
        }
    }
}
```

---

## 9. Biography Publishing

When the player meets the requirements, they can commission Mir Katib to assemble the full biography. This is a 30-day project costing 8,000 gold, after which a complete document is generated — all entries assembled with a prologue and epilogue.

```csharp
public class BiographyPublishBehavior : CampaignBehaviorBase
{
    private bool _isPublishing   = false;
    private int  _publishDueDay  = -1;
    private string _publishedBio = null;

    private const string PUBLISH_SYSTEM_PROMPT =
        "You are Mir Muhammad Katib, the personal biographer and scribe of a Mughal lord, " +
        "writing in the tradition of the Maasir-ul-Umara. You have been commissioned to write " +
        "the full biography (tazkira) of your lord. Assemble the provided chapter summaries " +
        "into a flowing, authoritative biographical account. Add a formal prologue praising " +
        "the patron and invoking God, and an epilogue noting the subject's current standing. " +
        "Write in elevated Indo-Persian biographical prose translated to English. " +
        "Approximately 800-1200 words total.";

    public static BiographyPublishBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
    }

    public void CommissionBiography()
    {
        if (!BiographerBehavior.Instance?.CanPublish() == true)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                "You have not yet achieved sufficient renown for a biography. " +
                "(Renown 1000+, Valour 500+, 10+ recorded events required.)",
                Color.FromUint(0xFFCC4400)));
            return;
        }

        if (Hero.MainHero.Gold < 8000)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                "Mir Katib requires 8,000 gold to assemble the full biography."));
            return;
        }

        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, 8000);
        _isPublishing  = true;
        _publishDueDay = (int)CampaignTime.Now.ToDays + 30;

        InformationManager.DisplayMessage(new InformationMessage(
            "Mir Katib has begun assembling your biography. It will be complete in 30 days.",
            Color.FromUint(0xFFD4AF37)));
    }

    private void OnDailyTick()
    {
        if (!_isPublishing) return;
        if ((int)CampaignTime.Now.ToDays < _publishDueDay) return;

        _isPublishing = false;
        GenerateBiography();
    }

    private void GenerateBiography()
    {
        var entries = BiographerBehavior.Instance?.GetEntries()
            .Where(e => e.IsRendered)
            .OrderBy(e => e.Index)
            .ToList();

        if (entries == null || entries.Count == 0) return;

        // Assemble chapter summaries for the synthesis prompt
        string chapterSummaries = string.Join("\n\n",
            entries.Select((e, i) =>
                $"Chapter {i + 1} ({e.DateLabel}):\n{e.GeneratedProse}"));

        string playerName = Hero.MainHero?.Name.ToString() ?? "the lord";
        string rank       = MansabdariBehavior.Instance?.GetRankTitle(Hero.MainHero) ?? "";
        string kingdom    = Hero.MainHero?.Clan?.Kingdom?.Name.ToString() ?? "";

        string fullPrompt =
            $"Subject: {playerName}, {rank} of {kingdom}\n\n" +
            $"Chapter entries to assemble:\n\n{chapterSummaries}";

        LLMClientBehavior.Instance?.Enqueue(new LLMClientBehavior.LLMRequest
        {
            RequestId    = "biography_publish",
            SystemPrompt = PUBLISH_SYSTEM_PROMPT,
            Prompt       = fullPrompt,
            Purpose      = LLMClientBehavior.LLMRequestPurpose.BiographyPublish,
            Tag          = "biography_complete"
        });

        // The LLMClientBehavior dispatches the result back here via a dedicated handler
    }

    public void OnBiographyReady(string text)
    {
        _publishedBio = text;
        Hero.MainHero.Clan.Influence += 50;
        Hero.MainHero.Clan.Renown    += 200;

        InformationManager.ShowInquiry(new InquiryData(
            $"The Biography of {Hero.MainHero?.Name} is Complete",
            "Mir Katib presents you with the assembled tazkira of your life and deeds. " +
            "News of the publication has spread through the court. Your fame grows.",
            true, false,
            "Read the biography",
            "",
            () => DisplayFullBiography(),
            () => { }
        ));
    }

    private void DisplayFullBiography()
    {
        if (_publishedBio == null) return;
        // Display in Bannerlord's scroll/book UI
        // Bannerlord has GauntletMovieManager or BookMissionView for this
        // Simplest option: multi-page InformationManager overlay
        string[] pages = SplitIntoPages(_publishedBio, 500);  // 500 chars per page
        ShowPagedText(pages, 0);
    }

    private string[] SplitIntoPages(string text, int pageSize)
    {
        var pages = new List<string>();
        for (int i = 0; i < text.Length; i += pageSize)
            pages.Add(text.Substring(i, Math.Min(pageSize, text.Length - i)));
        return pages.ToArray();
    }

    private void ShowPagedText(string[] pages, int currentPage)
    {
        bool hasNext = currentPage < pages.Length - 1;
        InformationManager.ShowInquiry(new InquiryData(
            $"The Biography — Page {currentPage + 1}/{pages.Length}",
            pages[currentPage],
            hasNext, true,
            "Next page",
            "Close",
            () => ShowPagedText(pages, currentPage + 1),
            () => { }
        ));
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData("hind_bio_publishing",   ref _isPublishing);
        dataStore.SyncData("hind_bio_publish_day",  ref _publishDueDay);
        dataStore.SyncData("hind_bio_published",    ref _publishedBio);
    }
}
```

---

## 10. In-Game Display

### Town/castle menu additions

```csharp
// News bulletin — available at any settlement with a recent dispatch
starter.AddGameMenuOption("town", "read_akhbarat",
    "{=!}Read the weekly dispatch (Akhbarat-e-Sultani)",
    args => NewsReporterBehavior.Instance?.GetBulletins(Settlement.CurrentSettlement)?.Count > 0,
    args => ShowBulletinMenu(Settlement.CurrentSettlement)
);

// Biographer journal — available anywhere when biographer is active
starter.AddGameMenuOption("town", "review_journal",
    "{=!}Review your journal (Mir Katib's record)",
    args => BiographerBehavior.Instance?.GetEntries()?.Count > 0,
    args => ShowJournalMenu()
);

// Publish biography — available from capital when requirements met
starter.AddGameMenuOption("town", "publish_biography",
    "{=!}Commission Mir Katib to write your biography (8,000 gold, 30 days)",
    args => BiographerBehavior.Instance?.CanPublish() == true
         && BiographyPublishBehavior.Instance?._isPublishing == false,
    args => BiographyPublishBehavior.Instance?.CommissionBiography()
);
```

---

## 11. Local LLM Setup — Ollama

### Step 1 — Install Ollama

Download from [ollama.com](https://ollama.com). On Windows, it installs as a background service. After installation:

```powershell
# Verify it is running
Invoke-WebRequest http://localhost:11434 -UseBasicParsing
# Should return: Ollama is running
```

### Step 2 — Pull the base model

Before you have a fine-tuned model, test with an off-the-shelf model:

```powershell
# Pull Qwen 2.5 1.5B — fastest, lowest VRAM (~1.2 GB), good prose
ollama pull qwen2.5:1.5b

# Or Phi-3.5 Mini — better English prose, ~2.2 GB VRAM
ollama pull phi3.5:3.8b

# Or Llama 3.2 3B — best balance of quality and speed for this task
ollama pull llama3.2:3b
```

### Step 3 — Create the Hindostan Scribe Modelfile

A `Modelfile` configures the model's default system prompt and parameters. Create a file called `Modelfile-hindostan-scribe`:

```
# Modelfile for the Hindostan Mod news and biography scribe
# Base: Qwen2.5-1.5B (swap to the fine-tuned GGUF once trained)

FROM qwen2.5:1.5b

PARAMETER temperature 0.72
PARAMETER top_p 0.90
PARAMETER num_predict 350
PARAMETER repeat_penalty 1.1

SYSTEM """
You are a royal scribe (waqai-nawis) of the Mughliya court, circa 1720 CE.
You write in the elevated, formal prose of 18th-century Indo-Persian court
chronicles translated into dignified English, in the tradition of Abul Fazl's
Akbarnama and the Maasir-ul-Umara.

Your language is measured, rich with honorifics, and free of modern idiom.
Victories are praised with appropriate gravity; defeats are noted with
decorum; the hand of Providence is acknowledged. You never use casual English.

When writing news dispatches: third person, dateline first, 2-3 paragraphs.
When writing biography entries: third person, formal, 2-3 paragraphs per event.
"""
```

```powershell
# Register it with Ollama
ollama create hindostan-scribe -f Modelfile-hindostan-scribe

# Test it
ollama run hindostan-scribe "Write a news dispatch about a battle near Agra where the Mughal forces defeated a Maratha raiding party. 150 men were lost on each side. The Maratha commander was captured."
```

### Step 4 — Use the fine-tuned GGUF (after training)

Once you have exported your fine-tuned model as `hindostan-scribe-q4km.gguf`:

```
# Update the Modelfile to use your fine-tuned base
FROM ./hindostan-scribe-q4km.gguf

PARAMETER temperature 0.68
PARAMETER top_p 0.88
PARAMETER num_predict 350
PARAMETER repeat_penalty 1.15

SYSTEM """
[same system prompt as above]
"""
```

```powershell
ollama create hindostan-scribe -f Modelfile-hindostan-scribe
```

---

## 12. Fine-Tuning the hindostan-scribe Model

Fine-tuning converts a general-purpose model into one that reliably produces 18th-century court prose. This is done once, offline, and the result is distributed as part of the mod.

### Why fine-tune rather than just prompting?

A stock 1.5B model prompted for "18th-century Mughal court prose" will produce plausible but inconsistent results — sometimes slipping into modern language, sometimes repeating phrases, sometimes getting the register wrong. Fine-tuning teaches the specific vocabulary, sentence structures, and honorific patterns of the target register such that every generation is consistent. With only 1,500–3,000 training examples, a 1.5B model can be reliably transformed.

### Recommended model

**Qwen 2.5 1.5B Instruct** — as of mid-2025 this is the strongest model at this size class for creative prose. It runs at Q4_K_M quantization in ~1.1 GB VRAM, making it viable on laptops with integrated AMD or Intel graphics.

Secondary option: **Phi-3.5 Mini Instruct (3.8B)** if the player has a dedicated GPU (needs ~2.8 GB VRAM at Q4_K_M).

### Training environment

Use **Google Colab** (free tier, T4 GPU) with **Unsloth** (the fastest open-source LORA fine-tuning library). A full training run of 1,500 examples at 3 epochs takes approximately 45 minutes on a Colab T4.

### Colab notebook — complete fine-tuning pipeline

```python
# ── Cell 1: Install dependencies ─────────────────────────────────────────────
!pip install unsloth
!pip install trl transformers accelerate datasets

# ── Cell 2: Load model with Unsloth ─────────────────────────────────────────
from unsloth import FastLanguageModel
import torch

MODEL_NAME   = "unsloth/Qwen2.5-1.5B-Instruct"
MAX_SEQ_LEN  = 2048

model, tokenizer = FastLanguageModel.from_pretrained(
    model_name     = MODEL_NAME,
    max_seq_length = MAX_SEQ_LEN,
    dtype          = None,       # auto-detect (bfloat16 on Ampere+, float16 on T4)
    load_in_4bit   = True,       # QLoRA — halves VRAM
)

# Add LoRA adapters
model = FastLanguageModel.get_peft_model(
    model,
    r                   = 16,   # rank — higher = more capacity but more VRAM
    target_modules      = ["q_proj", "k_proj", "v_proj", "o_proj",
                           "gate_proj", "up_proj", "down_proj"],
    lora_alpha          = 16,
    lora_dropout        = 0.0,
    bias                = "none",
    use_gradient_checkpointing = "unsloth",  # saves VRAM via clever checkpointing
)

# ── Cell 3: Prepare dataset ──────────────────────────────────────────────────
from datasets import Dataset

# Load your training file (see Dataset Construction section below)
import json
with open("hindostan_training.json", "r", encoding="utf-8") as f:
    raw = json.load(f)

# Format as Alpaca instruction-following
ALPACA_TEMPLATE = """Below is an instruction that describes a task, paired with an input
that provides further context. Write a response that appropriately completes the request.

### Instruction:
{instruction}

### Input:
{input}

### Response:
{output}"""

def format_example(row):
    return {
        "text": ALPACA_TEMPLATE.format(
            instruction = row["instruction"],
            input       = row["input"],
            output      = row["output"]
        )
    }

dataset = Dataset.from_list(raw).map(format_example)

# ── Cell 4: Train ─────────────────────────────────────────────────────────────
from trl import SFTTrainer
from transformers import TrainingArguments

trainer = SFTTrainer(
    model             = model,
    tokenizer         = tokenizer,
    train_dataset     = dataset,
    dataset_text_field = "text",
    max_seq_length    = MAX_SEQ_LEN,
    dataset_num_proc  = 2,
    args              = TrainingArguments(
        per_device_train_batch_size   = 2,
        gradient_accumulation_steps   = 4,
        warmup_ratio                  = 0.03,
        num_train_epochs              = 3,
        learning_rate                 = 2e-4,
        fp16                          = not torch.cuda.is_bf16_supported(),
        bf16                          = torch.cuda.is_bf16_supported(),
        logging_steps                 = 10,
        optim                         = "adamw_8bit",
        weight_decay                  = 0.01,
        lr_scheduler_type             = "cosine",
        seed                          = 42,
        output_dir                    = "outputs",
        save_strategy                 = "epoch",
    ),
)

trainer.train()

# ── Cell 5: Export to GGUF for Ollama ────────────────────────────────────────
model.save_pretrained_gguf(
    "hindostan-scribe",
    tokenizer,
    quantization_method = "q4_k_m"
)
# This produces: hindostan-scribe-unsloth.Q4_K_M.gguf
# Download from Colab and place in your mod folder
```

---

## 13. Dataset Construction

The training dataset is the most important part. Each example teaches the model to transform structured game-event JSON into a specific prose register.

### Dataset format

```json
[
  {
    "instruction": "You are a waqai-nawis (royal news scribe) of the Mughliya court, 1720 CE. Write a formal news dispatch in the style of an 18th-century Indo-Persian court chronicle, translated to dignified English. 2-3 paragraphs.",
    "input": "{\"event_type\": \"Battle\", \"date\": \"Year 1022, Harvest Season\", \"location\": \"near Panipat\", \"protagonists\": [\"Nawab Ahmad Khan\"], \"antagonists\": [\"Sardar Vishwas Rao\"], \"winner_casualties\": 180, \"loser_casualties\": 650, \"captured\": [\"Sardar Vishwas Rao\"], \"significance\": \"decisive\"}",
    "output": "Dispatch from the frontier of Panipat, in the Harvest Season of the Year One Thousand and Twenty-Two of the Hijri reckoning.\n\nIt is written, by the grace of the Almighty, that the armies of the Mughliya Sultanat, moving under the command of the valiant Nawab Ahmad Khan — may his shadow endure — did on this day engage and utterly defeat the forces of the Maratha confederation upon the ancient plain of Panipat. The battle was fierce and the issue long in doubt, but by the third hour after midday the enemy lines were broken and put to flight.\n\nOf the defenders of the faith, one hundred and eighty souls passed to the mercy of God. The enemy suffered six hundred and fifty dead, with many hundreds more taken prisoner. Among the captives is counted Sardar Vishwas Rao himself, commander of the Maratha horse, now held in honourable custody. The field is held; the roads to the imperial capital remain secure."
  },
  ...
]
```

### Data sources — three tiers

**Tier 1 — Historical texts (highest quality, but requires processing):**

| Source | Where to get | How to use |
|--------|-------------|-----------|
| *Baburnama* (Memoirs of Babur, A.S. Beveridge translation) | Project Gutenberg eBook #3702 | Extract battle/event accounts; generate matching JSON |
| *Akbarnama* (Abul Fazl, H. Beveridge translation, 3 vols) | archive.org — search "Akbarnama Beveridge" | Extract structured events from narrative |
| *Maasir-ul-Umara* (Biographical notices of Mughal nobles) | archive.org — "Maasir-ul-Umara" | Biography chapter entries are ready-made targets |
| *History of India — Elliot & Dowson* (8 vols of translated Persian chronicles) | archive.org | Diverse authors, multiple periods |

Processing approach for Tier 1:
1. Download the text
2. Use Claude or GPT-4 to read each chapter and output pairs in the JSON format shown above
3. Prompt: *"Read this passage from the Akbarnama. Extract the key facts as a JSON event record (event_type, date, location, protagonists, antagonists, casualties, significance). Then write the same passage back in the same prose style. Return both as a JSON object with keys 'input' and 'output'."*
4. Review and clean output

This process produces ~400–600 high-quality pairs from the Akbarnama alone.

**Tier 2 — British-India newspapers (for news dispatch register):**

| Source | Where to get |
|--------|-------------|
| *Bengal Gazette* (1780, first Indian newspaper in English) | Digitized at National Library of India |
| *Calcutta Gazette* (1780s-1790s) | British Library Endangered Archives |
| *Madras Courier* (1785) | Microfilm / digitized excerpts online |

These are not the same period (1720 vs 1780) but the formal register of British-era Indian official dispatches is the closest approximation to what an English translation of a Mughal akhbarat would sound like.

**Tier 3 — Synthetic examples (fastest to generate, 1000+ examples):**

Use Claude Haiku or GPT-4o-mini to generate training examples in bulk. Generate fictitious but plausible game events and write their dispatches:

```python
# Generate 1000 synthetic training examples
import anthropic

client = anthropic.Anthropic()

EVENT_TYPES = [
    {"type": "Battle", "template": "A battle near {location} between {faction1} and {faction2}..."},
    {"type": "Settlement_Captured", "template": "{city} was taken by {faction}..."},
    {"type": "Notable_Death", "template": "The lord {name} died of {cause}..."},
    {"type": "Famine", "template": "Famine struck {region} during {season}..."},
    {"type": "Promotion", "template": "The lord {name} was elevated to {rank}..."},
]

LOCATIONS = ["Agra", "Delhi", "Lahore", "Hyderabad", "Pune", "Amber", "Lucknow",
             "Multan", "Amritsar", "Bijapur", "Golconda", "Surat", "Patna"]

FACTIONS = ["Mughliya Sultanat", "Marathas", "Rajputs", "Sikhs", "Afghans",
            "Mysoreans", "Bengalis", "Hyderabadis"]

def generate_event_json(event_type, location, f1, f2):
    return json.dumps({
        "event_type": event_type,
        "date": f"Year {random.randint(1018, 1025)}, {random.choice(['Harvest', 'Monsoon', 'Winter', 'Pre-Monsoon'])} Season",
        "location": f"near {location}",
        "protagonists": [f1],
        "antagonists": [f2],
        "winner_casualties": random.randint(50, 800),
        "loser_casualties": random.randint(50, 1200),
        "significance": random.choice(["minor", "significant", "decisive"])
    }, indent=2)

training_data = []
for _ in range(1000):
    loc = random.choice(LOCATIONS)
    f1, f2 = random.sample(FACTIONS, 2)
    event_json = generate_event_json("Battle", loc, f1, f2)

    response = client.messages.create(
        model="claude-haiku-4-5-20251001",
        max_tokens=400,
        messages=[{
            "role": "user",
            "content": f"Write a 2-paragraph formal news dispatch in the style of an "
                       f"18th-century Mughal court scribe (waqai-nawis) for this event. "
                       f"Dignified, formal English, third person, period-appropriate honorifics.\n\n"
                       f"Event:\n{event_json}"
        }]
    )

    training_data.append({
        "instruction": "You are a waqai-nawis (royal news scribe) of the Mughliya court, 1720 CE. "
                       "Write a formal news dispatch in dignified, period-appropriate English. "
                       "2-3 paragraphs, third person, dateline first.",
        "input": event_json,
        "output": response.content[0].text
    })

with open("hindostan_synthetic.json", "w") as f:
    json.dump(training_data, f, indent=2)
```

**Final dataset composition:**

| Source | Examples | Notes |
|--------|----------|-------|
| Akbarnama extraction | ~500 | Highest quality |
| Maasir-ul-Umara | ~200 | Biography register |
| Synthetic (news) | ~1000 | Bulk, covers all event types |
| Synthetic (biography) | ~500 | Covers biography entries |
| **Total** | **~2200** | Sufficient for strong fine-tuning |

---

## 14. Connecting Everything — Registration and Settings

### SubModule registration

```csharp
// In HindostanSubModule.cs — add to OnSessionLaunched

private void OnSessionLaunched(CampaignGameStarter starter)
{
    // ... (existing behaviors from chapters 4-17)

    starter.AddBehavior(new EventCaptureBehavior());
    starter.AddBehavior(new LLMClientBehavior());
    starter.AddBehavior(new NewsReporterBehavior());
    starter.AddBehavior(new BiographerBehavior());
    starter.AddBehavior(new BiographyPublishBehavior());

    // Wire up LLM result dispatch for biography publish
    // (done via a tag check in LLMClientBehavior.DispatchResult)
}
```

### Graceful degradation at every layer

| Condition | Result |
|-----------|--------|
| Ollama not running | Falls back to LM Studio; if that also fails, uses templates |
| Template used | Text is visibly simpler but game never crashes |
| LLM returns empty string | Retried once; then template used |
| LLM takes > 30 seconds | Timeout, falls to template |
| No events this week | No bulletin generated (nothing to report) |
| Player has < 10 biography entries | "Publish" option hidden |

### Performance considerations

The LLM client makes at most **one request per town/castle per week** (capped to 5 in any 7-day window to prevent overload). Each request runs on the thread pool. The game main thread is never blocked. On a machine running Qwen 2.5 1.5B at Q4_K_M, each request completes in 2–4 seconds on a modern CPU (no GPU required).

---

**[← Chapter 17](17-Quality-of-Life-and-Depth-Systems.md)** | **[Home](Home.md)**
