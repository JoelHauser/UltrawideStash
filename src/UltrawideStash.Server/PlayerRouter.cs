using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace UltrawideStash.Server;

/// <summary>What the probe sends from its Awake, at every game start.</summary>
public record HelloRequest : IRequestData
{
    [JsonPropertyName("screen")]
    public string? Screen { get; init; }

    [JsonPropertyName("widenStashPanel")]
    public bool WidenStashPanel { get; init; } = true;

    [JsonPropertyName("gearPanelReserve")]
    public float GearPanelReserve { get; init; } = ClientSettings.DefaultReserve;

    [JsonPropertyName("probeVersion")]
    public string? ProbeVersion { get; init; }
}

/// <summary>The probe's measurement: the same fields as <see cref="Measurement"/>.</summary>
public record MeasuredRequest : IRequestData
{
    [JsonPropertyName("maxColumns")]
    public int MaxColumns { get; init; }

    [JsonPropertyName("screen")]
    public string? Screen { get; init; }

    [JsonPropertyName("canvasWidth")]
    public int CanvasWidth { get; init; }

    [JsonPropertyName("panelWidth")]
    public double PanelWidth { get; init; }

    [JsonPropertyName("canWiden")]
    public bool? CanWiden { get; init; }

    [JsonPropertyName("whyNot")]
    public string? WhyNot { get; init; }

    [JsonPropertyName("measuredAtUtc")]
    public string? MeasuredAtUtc { get; init; }

    [JsonPropertyName("probeVersion")]
    public string? ProbeVersion { get; init; }
}

/// <summary>
/// The answer the probe logs: one line, so a player's BepInEx log carries the server's
/// verdict. <see cref="Shapes"/>, on the hello only, is this player's five stash sizes
/// (<c>WxH</c> by template id) for the probe to apply to the game's templates.
/// </summary>
public record PlayerReply(string Message, Dictionary<string, string>? Shapes = null);

/// <summary>
/// The two routes each player's probe reports in on. See <see cref="PlayerRecord"/> for
/// why the width is per player, and <see cref="PlayerStashes"/> for what is done with it.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public class PlayerRouter(JsonUtil jsonUtil, PlayerStashes players, HttpResponseUtil httpResponseUtil)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<HelloRequest>(
                "/ultrawidestash/hello",
                async (url, info, sessionId, output, cancellationToken) =>
                    httpResponseUtil.NoBody(new PlayerReply(
                        await players.Hello(sessionId, info, cancellationToken), players.ShapesFor(sessionId)))),

            new RouteAction<MeasuredRequest>(
                "/ultrawidestash/measured",
                (url, info, sessionId, output, cancellationToken) =>
                    new ValueTask<string>(httpResponseUtil.NoBody(new PlayerReply(players.Measured(sessionId, info))))),
        ])
{
}
