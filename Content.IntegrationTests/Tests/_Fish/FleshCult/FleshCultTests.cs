#nullable enable
using System.Linq;
using Content.Client.Chat;
using Content.Client.UserInterface.Systems.Chat;
using Content.Server._Sunrise.Chat;
using Content.Server._Sunrise.FleshCult;
using Content.Server._Sunrise.FleshCult.GameRule;
using Content.Server.Actions;
using Content.Server.Antag;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Roles;
using Content.Shared._Sunrise.CollectiveMind;
using Content.Shared._Sunrise.FleshCult;
using Content.Shared.Actions.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DragDrop;
using Content.Shared.FixedPoint;
using Content.Shared.Flesh;
using Content.Shared.GameTicking;
using Content.Shared.Implants;
using Content.Shared.Mindshield;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Overlays;
using Content.Shared.Slippery;
using Content.Shared.Speech;
using Content.Shared.Weapons.Melee;
using Robust.Client.UserInterface;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using ServerChatSystem = Content.Server.Chat.Systems.ChatSystem;

namespace Content.IntegrationTests.Tests._Fish.FleshCult;

[TestFixture]
public sealed partial class FleshCultTests
{
    private static readonly EntProtoId FleshCultRuleId = "FleshCult";
    private static readonly EntProtoId DevourActionId = "FleshCultistDevour";

    [Test]
    public async Task FleshCultRule_DistributesRoles_WhenStartedInRound()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true,
            DummyTicker = false,
            Connected = true,
            InLobby = true,
        });

        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();
        var roleSys = server.System<RoleSystem>();
        var mindSys = server.System<MindSystem>();

        var dummies = await server.AddDummySessions(2);
        await pair.RunTicksSync(5);

        await pair.SetAntagPreference("FleshCultist", true);
        await pair.SetAntagPreference("FleshCultistLeader", true);

        await server.WaitPost(() =>
        {
            ticker.ToggleReadyAll(true);
            ticker.StartRound();
        });
        await pair.RunTicksSync(10);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));

        EntityUid ruleEnt = default;
        await server.WaitPost(() =>
        {
            ruleEnt = ticker.AddGameRule(FleshCultRuleId);
            ticker.StartGameRule(ruleEnt);
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent<FleshCultRuleComponent>(ruleEnt, out var ruleComp), Is.True);
            Assert.That(ruleComp!.Cultists, Is.Not.Empty, "FleshCult rule should distribute at least one cultist role when started in-round.");

            var player = pair.Player!.AttachedEntity!.Value;
            var anyHasCultistComp = dummies
                .Select(d => d.AttachedEntity)
                .Append(player)
                .Any(e => e != null && entMan.HasComponent<FleshCultistComponent>(e.Value));

            Assert.That(anyHasCultistComp, Is.True);

            var cultistUid = ruleComp.Cultists[0];
            Assert.That(mindSys.TryGetMind(cultistUid, out var mindId, out _), Is.True);
            Assert.That(roleSys.MindIsAntagonist(mindId), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FleshCult_PredatorRace_AllowedForAntagSelectionAndVirusConversion()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true,
            DummyTicker = false,
            Connected = true,
            InLobby = false,
        });

        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var antagSys = server.System<AntagSelectionSystem>();
        var fleshSys = server.System<FleshCultSystem>();
        var mindSys = server.System<MindSystem>();
        var roleSys = server.System<RoleSystem>();
        var solutionSys = server.System<SharedSolutionContainerSystem>();

        EntityUid predator = default;
        await server.WaitPost(() =>
        {
            predator = entMan.Spawn("MobPredator", map.MapCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, predator);
            var mind = mindSys.CreateMind(pair.Player!.UserId, "Yautja Cultist");
            mindSys.TransferTo(mind, predator, true);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(antagSys.IsEntityValid(predator, "SunriseFleshCultist"), Is.True,
                    "Predator (Yautja) should be valid for SunriseFleshCultist.");
                Assert.That(antagSys.IsEntityValid(predator, "SunriseFleshCultistLeader"), Is.True,
                    "Predator (Yautja) should be valid for SunriseFleshCultistLeader.");
            });

            var bloodstream = entMan.GetComponent<BloodstreamComponent>(predator);
            Assert.That(solutionSys.TryGetSolution(predator, bloodstream.BloodSolutionName, out var bloodSoln, out _), Is.True);
            Assert.That(solutionSys.TryAddReagent(bloodSoln!.Value, "Carol", FixedPoint2.New(10)), Is.True);
            // TryAddReagent triggers SolutionChangedEvent -> OnBloodstreamSolutionChanged -> TryInfectWithCarol automatically.
            Assert.That(entMan.TryGetComponent<PendingFleshCultistComponent>(predator, out var pending), Is.True,
                "Adding Carol reagent to Predator bloodstream should trigger PendingFleshCultist infection.");

            pending!.CurrentStage = PendingFleshCultistStage.Second;
            pending.Accumulator = pending.SecondStageTimer + 1f;
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<FleshCultistComponent>(predator), Is.True,
                    "Predator infected with Carol should become a FleshCultist.");
                Assert.That(entMan.HasComponent<FleshAbilitiesComponent>(predator), Is.True);
                Assert.That(mindSys.TryGetMind(predator, out var mindId, out _), Is.True);
                Assert.That(roleSys.MindHasRole<FleshCultistRoleComponent>(mindId), Is.True);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FleshCult_MindShield_BlocksSelectionAndCarolInfection()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var antagSys = server.System<AntagSelectionSystem>();
        var fleshSys = server.System<FleshCultSystem>();
        var implantSys = server.System<SharedSubdermalImplantSystem>();
        var mindShieldSys = server.System<MindShieldSystem>();
        var solutionSys = server.System<SharedSolutionContainerSystem>();

        EntityUid shieldedMob = default;
        EntityUid fakeShieldMob = default;

        await server.WaitPost(() =>
        {
            shieldedMob = entMan.Spawn("MobHuman", map.MapCoords);
            implantSys.AddImplant(shieldedMob, "MindShieldImplant");

            fakeShieldMob = entMan.Spawn("MobHuman", map.MapCoords);
            implantSys.AddImplant(fakeShieldMob, "FakeMindShieldImplant");
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            mindShieldSys.GetMindshieldStatus(shieldedMob, out var isShielded, out _);
            Assert.That(isShielded, Is.True);

            mindShieldSys.GetMindshieldStatus(fakeShieldMob, out var isFakeShielded, out _);
            Assert.That(isFakeShielded, Is.False);

            Assert.Multiple(() =>
            {
                Assert.That(antagSys.IsEntityValid(shieldedMob, "SunriseFleshCultist"), Is.False,
                    "MindShielded mob must not be valid for SunriseFleshCultist.");
                Assert.That(antagSys.IsEntityValid(shieldedMob, "SunriseFleshCultistLeader"), Is.False,
                    "MindShielded mob must not be valid for SunriseFleshCultistLeader.");
                Assert.That(antagSys.IsEntityValid(fakeShieldMob, "SunriseFleshCultist"), Is.True,
                    "FakeMindShield must not grant real antagonist immunity.");
            });

            var bloodstream = entMan.GetComponent<BloodstreamComponent>(shieldedMob);
            Assert.That(solutionSys.TryGetSolution(shieldedMob, bloodstream.BloodSolutionName, out var bloodSoln, out _), Is.True);
            Assert.That(solutionSys.TryAddReagent(bloodSoln!.Value, "Carol", FixedPoint2.New(10)), Is.True);

            Assert.That(fleshSys.CanInfectWithCarol((shieldedMob, bloodstream), bloodSoln.Value), Is.False,
                "MindShielded mob must not be infected with Carol.");
            Assert.That(fleshSys.TryInfectWithCarol((shieldedMob, bloodstream), bloodSoln.Value), Is.False);
            Assert.That(entMan.HasComponent<PendingFleshCultistComponent>(shieldedMob), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FleshHeart_AbsorbCorpse_DoesNotDropActionEntities()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var actionsSys = server.System<ActionsSystem>();
        var mobStateSys = server.System<MobStateSystem>();
        var xformSys = server.System<SharedTransformSystem>();
        var containerSys = server.System<SharedContainerSystem>();

        EntityUid heart = default;
        EntityUid cultist = default;
        EntityUid corpse = default;

        await server.WaitPost(() =>
        {
            var coords = map.GridCoords;
            heart = entMan.SpawnEntity("FleshHeart", coords);
            var heartXform = entMan.GetComponent<TransformComponent>(heart);
            if (!heartXform.Anchored)
                xformSys.AnchorEntity(heart, heartXform);
            Assert.That(heartXform.Anchored, Is.True,
                "FleshHeart must be anchored on test grid.");

            cultist = entMan.SpawnEntity("MobHuman", coords);
            entMan.EnsureComponent<FleshCultistComponent>(cultist);

            corpse = entMan.SpawnEntity("MobPredator", coords);
            EntityUid? extraAction = null;
            actionsSys.AddAction(corpse, ref extraAction, DevourActionId);
            mobStateSys.ChangeMobState(corpse, MobState.Dead);
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            var heartComp = entMan.GetComponent<FleshHeartComponent>(heart);
            heartComp.EntryDelay = 0.05f;

            var ev = new DragDropTargetEvent(cultist, corpse);
            entMan.EventBus.RaiseLocalEvent(heart, ref ev);
            Assert.That(ev.Handled, Is.True, "FleshHeart should accept drag-drop of dead Predator corpse.");
        });

        await pair.RunSeconds(1.0f);

        await server.WaitAssertion(() =>
        {
            var heartComp = entMan.GetComponent<FleshHeartComponent>(heart);
            Assert.That(heartComp.BodyContainer.ContainedEntities, Does.Contain(corpse),
                "Predator corpse should be absorbed by FleshHeart.");

            var actionQuery = entMan.EntityQueryEnumerator<ActionComponent>();
            while (actionQuery.MoveNext(out var actionUid, out _))
            {
                Assert.That(containerSys.IsEntityInContainer(actionUid), Is.True,
                    $"Action entity {entMan.ToPrettyString(actionUid)} should not be dropped onto the floor by FleshHeart.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FleshMobs_HaveNoSlip_StaminaResistance_NightVision_Flammable_And_PudgeDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var expectedNightVisionColor = Color.FromHex("#D61B1B26");

        string[] fleshMobProtos =
        [
            "MobFleshSpider",
            "MobFleshPudge",
            "MobFleshBall",
            "MobFleshBat",
            "MobFleshHugger",
            "MobFleshWorm",
            "MobSmallFleshWorm",
        ];

        await server.WaitAssertion(() =>
        {
            foreach (var proto in fleshMobProtos)
            {
                var mob = entMan.Spawn(proto, map.MapCoords);

                Assert.Multiple(() =>
                {
                    Assert.That(entMan.HasComponent<NoSlipComponent>(mob), Is.True,
                        $"{proto} should have NoSlipComponent.");
                    Assert.That(entMan.TryGetComponent<StaminaResistanceComponent>(mob, out var stamRes), Is.True,
                        $"{proto} should have StaminaResistanceComponent.");
                    Assert.That(stamRes!.DamageCoefficient, Is.EqualTo(0.5f).Within(0.001f),
                        $"{proto} should have 0.5 stamina damage coefficient.");
                    Assert.That(stamRes.Worn, Is.False,
                        $"{proto} StaminaResistance should apply directly to the mob (worn: false).");

                    Assert.That(entMan.TryGetComponent<NightVisionComponent>(mob, out var nightVision), Is.True,
                        $"{proto} should have NightVisionComponent matching BloodCult constructs.");
                    Assert.That(nightVision!.Enabled, Is.False);
                    Assert.That(nightVision.Action, Is.EqualTo(new EntProtoId("ActionToggleNightVision")));
                    Assert.That(nightVision.LightingColor, Is.EqualTo(expectedNightVisionColor));
                    Assert.That(nightVision.ActionEntity, Is.Not.Null,
                        $"{proto} should receive ActionToggleNightVision on startup.");

                    Assert.That(entMan.TryGetComponent<FlammableComponent>(mob, out var flammable), Is.True,
                        $"{proto} should inherit FlammableComponent from MobFlammable.");
                    Assert.That(flammable!.FireSpread, Is.True);
                    Assert.That(flammable.CanResistFire, Is.True);

                    Assert.That(entMan.TryGetComponent<CollectiveMindComponent>(mob, out var mind), Is.True,
                        $"{proto} should have CollectiveMindComponent.");
                    Assert.That(mind!.Minds, Does.Contain(new ProtoId<CollectiveMindPrototype>("FleshCult")));
                    Assert.That(entMan.HasComponent<SpeechComponent>(mob), Is.True,
                        $"{proto} should have SpeechComponent.");
                });

                if (proto == "MobFleshPudge")
                {
                    var melee = entMan.GetComponent<MeleeWeaponComponent>(mob);
                    Assert.Multiple(() =>
                    {
                        Assert.That(melee.Damage.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(20)));
                        Assert.That(melee.Damage.DamageDict["Blunt"], Is.EqualTo(FixedPoint2.New(30)));
                        Assert.That(melee.Damage.DamageDict["Structural"], Is.EqualTo(FixedPoint2.New(35)));
                    });
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FleshMob_CollectiveMindChat_And_IgniteToDeath_WorkWithoutErrors()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true,
            DummyTicker = false,
            Connected = true,
            InLobby = false,
        });

        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var chatSys = server.System<ServerChatSystem>();
        var flammableSys = server.System<FlammableSystem>();
        var dmgSys = server.System<DamageableSystem>();

        var mindListener = server.System<CollectiveMindListenerSystem>();

        EntityUid pudge = default;
        EntityUid cultistReceiver = default;

        await server.WaitPost(() =>
        {
            mindListener.LastEvent = null;
            pudge = entMan.Spawn("MobFleshPudge", map.MapCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, pudge);

            cultistReceiver = entMan.Spawn("MobHuman", map.MapCoords);
            entMan.EnsureComponent<FleshCultistComponent>(cultistReceiver);
        });
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var clientMindSys = client.System<CollectiveMindChatUpdateSystem>();
            var uiMan = client.ResolveDependency<IUserInterfaceManager>();
            var chatUi = uiMan.GetUIController<ChatUIController>();

            Assert.Multiple(() =>
            {
                Assert.That(clientMindSys.IsCollectiveMind, Is.True,
                    "Attached flesh mob player must be recognized as a collective mind speaker.");
                Assert.That(chatUi.CanSendChannels.HasFlag(ChatSelectChannel.CollectiveMind), Is.True,
                    "Client chat UI must allow CollectiveMind channel for controlled flesh mobs.");

                var (channel, _, _, mindProto) = chatUi.SplitInputContents("+п сигнал плоти");
                Assert.That(channel, Is.EqualTo(ChatSelectChannel.CollectiveMind));
                Assert.That(mindProto?.ID, Is.EqualTo("FleshCult"));
            });
        });

        await server.WaitAssertion(() =>
        {
            chatSys.TrySendInGameICMessage(
                pudge,
                "+п проверка связи",
                InGameICChatType.CollectiveMind,
                ChatTransmitRange.Normal,
                player: pair.Player);

            var capturedEvent = mindListener.LastEvent;
            Assert.That(capturedEvent, Is.Not.Null,
                "Flesh mob collective mind message should dispatch CollectiveMindSpokeEvent.");
            Assert.That(capturedEvent!.Source, Is.EqualTo(pudge));
            Assert.That(capturedEvent.CollectiveMindId, Is.EqualTo("FleshCult"));
            Assert.That(capturedEvent.Message, Is.EqualTo("Проверка связи."));
        });

        await server.WaitPost(() =>
        {
            flammableSys.AdjustFireStacks(pudge, 10, ignite: true);
            Assert.That(entMan.TryGetComponent<FlammableComponent>(pudge, out var flammable), Is.True);
            Assert.That(flammable!.OnFire, Is.True, "Flesh mob should ignite cleanly.");

            dmgSys.TryChangeDamage(pudge, new DamageSpecifier { DamageDict = { { "Heat", FixedPoint2.New(500) } } }, true);
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent<MobStateComponent>(pudge, out var state), Is.True);
            Assert.That(state!.CurrentState, Is.EqualTo(MobState.Dead),
                "Flesh mob ignited and killed by heat damage should die without crashing client or server.");
        });

        await pair.CleanReturnAsync();
    }

    private sealed partial class CollectiveMindListenerSystem : EntitySystem
    {
        public CollectiveMindSpokeEvent? LastEvent;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<CollectiveMindSpokeEvent>(OnCollectiveMindSpoke);
            SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        }

        private void OnCollectiveMindSpoke(CollectiveMindSpokeEvent ev)
        {
            LastEvent = ev;
        }

        private void OnRoundRestart(RoundRestartCleanupEvent ev)
        {
            LastEvent = null;
        }
    }
}
