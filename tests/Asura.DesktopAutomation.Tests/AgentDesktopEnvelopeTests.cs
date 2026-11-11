using System.Text;
using Asura.Application;

namespace Asura.DesktopAutomation.Tests;

public sealed class AgentDesktopEnvelopeTests
{
    [Fact]
    public void A_success_envelope_yields_its_data_object()
    {
        var envelope = Parse("""{"version":"2.6","ok":true,"command":"wait","data":{"found":true,"elapsed_ms":42}}""");

        Assert.Equal(AgentDesktopEnvelopeKind.Data, envelope.Kind);
        Assert.Null(envelope.ErrorCode);
        Assert.True(envelope.Data.GetProperty("found").GetBoolean());
        Assert.Equal(42, envelope.Data.GetProperty("elapsed_ms").GetInt32());
        Assert.Equal(AgentDesktopDelivery.Unknown, envelope.Delivery);
    }

    [Fact]
    public void An_error_envelope_yields_its_code_and_delivery()
    {
        var envelope = Parse(
            """
            {"version":"2.6","ok":false,"command":"click","error":{"code":"STALE_REF","message":"ignored",
             "details":{"name":"ignored too"},"disposition":{"delivery":"not_delivered","retry":"safe"}}}
            """);

        Assert.Equal(AgentDesktopEnvelopeKind.Error, envelope.Kind);
        Assert.Equal("STALE_REF", envelope.ErrorCode);
        Assert.Equal(AgentDesktopDelivery.NotDelivered, envelope.Delivery);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("2.6")]
    [InlineData("3.0")]
    [InlineData("not a version")]
    public void The_envelope_version_and_unknown_fields_do_not_change_how_it_is_read(string version)
    {
        // The ABI major the loader pins is the compatibility contract. The
        // envelope's own version only ever adds fields, and every field Asura
        // reads is checked for its exact kind.
        var json = "{\"version\":\"" + version + "\",\"ok\":true,\"command\":\"snapshot\",\"future\":[1,2],"
            + "\"data\":{\"snapshot_id\":\"s1\",\"future\":{}}}";

        var envelope = Parse(json);

        Assert.Equal(AgentDesktopEnvelopeKind.Data, envelope.Kind);
        Assert.Equal("s1", envelope.Data.GetProperty("snapshot_id").GetString());
    }

    [Fact]
    public void A_window_accessibility_does_not_expose_yet_is_a_window_not_found()
    {
        // Seen live right after the fixture launched: the window server listed
        // the window before accessibility exposed it.
        const string json = """
            {"version":"2.6","ok":false,"command":"snapshot","error":{"code":"ACTION_NOT_SUPPORTED",
            "message":"Window 'w-8831' exists but is not exposed through accessibility",
            "details":{"kind":"window_without_accessibility_element","window_id":"w-8831"},
            "disposition":{"delivery":"not_delivered","retry":"safe"}}}
            """;

        var envelope = AgentDesktopEnvelope.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Equal("WINDOW_NOT_FOUND", envelope.ErrorCode);
        Assert.Equal(AgentDesktopFailureCode.WindowNotFound, AgentDesktopEnvelope.MapErrorCode(envelope.ErrorCode));
        Assert.Equal(AgentDesktopDelivery.NotDelivered, envelope.Delivery);
    }

    [Theory]
    [InlineData("""{"kind":"other"}""")]
    [InlineData("""{"kind":7}""")]
    [InlineData("""[]""")]
    public void Any_other_unsupported_answer_stays_not_supported(string details)
    {
        var json = """{"ok":false,"error":{"code":"ACTION_NOT_SUPPORTED","details":""" + details + "}}";

        Assert.Equal("ACTION_NOT_SUPPORTED", AgentDesktopEnvelope.Parse(Encoding.UTF8.GetBytes(json)).ErrorCode);
    }

    [Fact]
    public void An_envelope_without_a_version_is_still_read()
    {
        Assert.Equal(AgentDesktopEnvelopeKind.Data, Parse("""{"ok":true,"data":{}}""").Kind);
    }

    [Theory]
    [InlineData("not_delivered", AgentDesktopDelivery.NotDelivered)]
    [InlineData("delivery_uncertain", AgentDesktopDelivery.Uncertain)]
    [InlineData("delivered_unverified", AgentDesktopDelivery.DeliveredUnverified)]
    [InlineData("delivered_verified", AgentDesktopDelivery.DeliveredVerified)]
    [InlineData("unknown", AgentDesktopDelivery.Unknown)]
    [InlineData("NOT_DELIVERED", AgentDesktopDelivery.Unknown)]
    [InlineData("delivered", AgentDesktopDelivery.Unknown)]
    [InlineData("", AgentDesktopDelivery.Unknown)]
    public void Delivery_is_read_from_the_closed_vocabulary_and_anything_else_is_unknown(
        string delivery,
        AgentDesktopDelivery expected)
    {
        var data = Parse("{\"ok\":true,\"data\":{\"disposition\":{\"delivery\":\"" + delivery + "\"}}}");
        var error = Parse(
            "{\"ok\":false,\"error\":{\"code\":\"TIMEOUT\",\"disposition\":{\"delivery\":\"" + delivery + "\"}}}");

        Assert.Equal(expected, data.Delivery);
        Assert.Equal(expected, error.Delivery);
    }

    [Theory]
    [InlineData("""{"ok":false,"error":{"code":"TIMEOUT"}}""")]
    [InlineData("""{"ok":false,"error":{"code":"TIMEOUT","disposition":null}}""")]
    [InlineData("""{"ok":false,"error":{"code":"TIMEOUT","disposition":"not_delivered"}}""")]
    [InlineData("""{"ok":false,"error":{"code":"TIMEOUT","disposition":{}}}""")]
    [InlineData("""{"ok":false,"error":{"code":"TIMEOUT","disposition":{"delivery":1}}}""")]
    [InlineData("""{"ok":false,"error":{"code":"TIMEOUT","disposition":{"delivery":["not_delivered"]}}}""")]
    public void A_missing_or_malformed_disposition_is_never_read_as_not_delivered(string json)
    {
        var envelope = Parse(json);

        Assert.Equal(AgentDesktopEnvelopeKind.Error, envelope.Kind);
        Assert.Equal(AgentDesktopDelivery.Unknown, envelope.Delivery);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("\"ok\"")]
    [InlineData("[]")]
    [InlineData("""[{"ok":true,"data":{}}]""")]
    [InlineData("{}")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"ok":"true","data":{}}""")]
    [InlineData("""{"ok":1,"data":{}}""")]
    [InlineData("""{"ok":null,"data":{}}""")]
    [InlineData("""{"ok":true}""")]
    [InlineData("""{"ok":true,"data":null}""")]
    [InlineData("""{"ok":true,"data":[]}""")]
    [InlineData("""{"ok":true,"data":"{}"}""")]
    [InlineData("""{"ok":true,"error":{"code":"TIMEOUT"}}""")]
    [InlineData("""{"ok":false}""")]
    [InlineData("""{"ok":false,"data":{}}""")]
    [InlineData("""{"ok":false,"error":null}""")]
    [InlineData("""{"ok":false,"error":"TIMEOUT"}""")]
    [InlineData("""{"ok":false,"error":{}}""")]
    [InlineData("""{"ok":false,"error":{"code":7}}""")]
    [InlineData("""{"ok":false,"error":{"code":null}}""")]
    [InlineData("""{"ok":true,"data":{}""")]
    [InlineData("""{"ok":true,"data":{}} trailing""")]
    [InlineData("""{"ok":true,"data":{}}{"ok":true,"data":{}}""")]
    [InlineData("""{'ok':true,'data':{}}""")]
    [InlineData("""{"ok":true,"data":{},}""")]
    [InlineData("""{"ok":true,/* comment */"data":{}}""")]
    public void Anything_that_is_not_exactly_an_envelope_is_invalid(string json)
    {
        var envelope = Parse(json);

        Assert.Equal(AgentDesktopEnvelopeKind.Invalid, envelope.Kind);
        Assert.Null(envelope.ErrorCode);
        Assert.Equal(AgentDesktopDelivery.Unknown, envelope.Delivery);
    }

    [Fact]
    public void Bytes_that_are_not_UTF8_are_invalid()
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes("""{"ok":true,"data":{"name":"""), (byte)'"', 0xC3, 0x28, (byte)'"', (byte)'}', (byte)'}'];

        Assert.Equal(AgentDesktopEnvelopeKind.Invalid, AgentDesktopEnvelope.Parse(bytes).Kind);
    }

    [Theory]
    [InlineData("""{"ok":false,"error":{"code":"@"}}""")]
    [InlineData("""{"ok":false,"error":{"code":"TIMEOUT","disposition":{"delivery":"@"}}}""")]
    [InlineData("""{"ok":true,"data":{"disposition":{"delivery":"@"}}}""")]
    public void An_escaped_lone_surrogate_in_a_field_the_envelope_reads_is_invalid_not_an_exception(string template)
    {
        var json = template.Replace("@", "\\uD83D", StringComparison.Ordinal);

        Assert.Equal(AgentDesktopEnvelopeKind.Invalid, Parse(json).Kind);
    }

    [Fact]
    public void An_envelope_nested_deeper_than_any_snapshot_Asura_requests_is_invalid_not_a_crash()
    {
        // 100,000 levels would overflow the stack of a recursive reader.
        var hostile = "{\"ok\":true,\"data\":" + Nested(100_000) + "}";
        var deepestAccepted = "{\"ok\":true,\"data\":" + Nested(127) + "}";
        var oneTooDeep = "{\"ok\":true,\"data\":" + Nested(128) + "}";

        Assert.Equal(AgentDesktopEnvelopeKind.Invalid, Parse(hostile).Kind);
        Assert.Equal(AgentDesktopEnvelopeKind.Data, Parse(deepestAccepted).Kind);
        Assert.Equal(AgentDesktopEnvelopeKind.Invalid, Parse(oneTooDeep).Kind);
    }

    [Fact]
    public void The_deepest_tree_Asura_requests_fits_the_accepted_nesting()
    {
        // data > tree > (children > node) per accessibility level.
        var tree = new StringBuilder("{\"role\":\"leaf\"}");
        for (var level = 0; level < AgentDesktopRequest.MaximumSnapshotDepth; level++)
        {
            tree.Insert(0, "{\"role\":\"group\",\"children\":[").Append("]}");
        }

        var envelope = Parse("{\"ok\":true,\"data\":{\"tree\":" + tree + "}}");

        Assert.Equal(AgentDesktopEnvelopeKind.Data, envelope.Kind);
    }

    [Fact]
    public void The_data_outlives_the_bytes_it_was_parsed_from()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"ok":true,"data":{"snapshot_id":"s1"}}""");
        var envelope = AgentDesktopEnvelope.Parse(bytes);

        Array.Clear(bytes);

        Assert.Equal("s1", envelope.Data.GetProperty("snapshot_id").GetString());
    }

    [Theory]
    [InlineData(AdResult.Ok, """{"ok":true,"data":{}}""", null)]
    [InlineData(AdResult.StaleRef, """{"ok":false,"error":{"code":"STALE_REF"}}""", "STALE_REF")]
    [InlineData(AdResult.Timeout, """{"ok":false,"error":{"code":"TIMEOUT"}}""", "TIMEOUT")]
    [InlineData(AdResult.Timeout, """{"ok":false,"error":{"code":"APP_UNRESPONSIVE"}}""", "APP_UNRESPONSIVE")]
    [InlineData(-99, """{"ok":false,"error":{"code":"SOMETHING_NEW"}}""", "SOMETHING_NEW")]
    public void A_status_that_agrees_with_its_envelope_keeps_it(int status, string json, string? expectedErrorCode)
    {
        var envelope = AgentDesktopEnvelope.Parse(Encoding.UTF8.GetBytes(json), status);

        Assert.Equal(
            expectedErrorCode is null ? AgentDesktopEnvelopeKind.Data : AgentDesktopEnvelopeKind.Error,
            envelope.Kind);
        Assert.Equal(expectedErrorCode, envelope.ErrorCode);
    }

    [Theory]
    [InlineData(AdResult.Ok, """{"ok":false,"error":{"code":"STALE_REF","disposition":{"delivery":"not_delivered"}}}""")]
    [InlineData(AdResult.StaleRef, """{"ok":true,"data":{"disposition":{"delivery":"delivered_verified"}}}""")]
    [InlineData(AdResult.Timeout, """{"ok":true,"data":{}}""")]
    [InlineData(-99, """{"ok":true,"data":{}}""")]
    [InlineData(1, """{"ok":true,"data":{}}""")]
    public void A_status_that_contradicts_its_envelope_is_no_answer_at_all(int status, string json)
    {
        var envelope = AgentDesktopEnvelope.Parse(Encoding.UTF8.GetBytes(json), status);

        // Neither half is believed: not the "not delivered" of a failure the
        // status calls a success, nor the data of a success the status denies.
        Assert.Equal(AgentDesktopEnvelopeKind.Invalid, envelope.Kind);
        Assert.Equal(AgentDesktopDelivery.Unknown, envelope.Delivery);
        Assert.Null(envelope.ErrorCode);
    }

    [Theory]
    [InlineData(AdResult.Ok)]
    [InlineData(AdResult.Timeout)]
    public void A_malformed_envelope_is_invalid_whatever_the_status(int status)
    {
        Assert.Equal(
            AgentDesktopEnvelopeKind.Invalid,
            AgentDesktopEnvelope.Parse(Encoding.UTF8.GetBytes("""{"ok":true"""), status).Kind);
    }

    [Theory]
    [InlineData("PERM_DENIED", AgentDesktopFailureCode.PermissionDenied)]
    [InlineData("ELEMENT_NOT_FOUND", AgentDesktopFailureCode.ElementNotFound)]
    [InlineData("APP_NOT_FOUND", AgentDesktopFailureCode.AppNotFound)]
    [InlineData("ACTION_FAILED", AgentDesktopFailureCode.ActionFailed)]
    [InlineData("ACTION_NOT_SUPPORTED", AgentDesktopFailureCode.ActionNotSupported)]
    [InlineData("STALE_REF", AgentDesktopFailureCode.StaleRef)]
    [InlineData("WINDOW_NOT_FOUND", AgentDesktopFailureCode.WindowNotFound)]
    [InlineData("PLATFORM_NOT_SUPPORTED", AgentDesktopFailureCode.Unavailable)]
    [InlineData("TIMEOUT", AgentDesktopFailureCode.Timeout)]
    [InlineData("INVALID_ARGS", AgentDesktopFailureCode.InvalidArguments)]
    [InlineData("NOTIFICATION_NOT_FOUND", AgentDesktopFailureCode.ActionFailed)]
    [InlineData("SNAPSHOT_NOT_FOUND", AgentDesktopFailureCode.SnapshotNotFound)]
    [InlineData("POLICY_DENIED", AgentDesktopFailureCode.PolicyDenied)]
    [InlineData("AMBIGUOUS_TARGET", AgentDesktopFailureCode.AmbiguousTarget)]
    [InlineData("APP_UNRESPONSIVE", AgentDesktopFailureCode.Timeout)]
    public void Every_upstream_error_code_maps_onto_the_closed_failure_set(string code, AgentDesktopFailureCode expected)
    {
        Assert.Equal(expected, AgentDesktopEnvelope.MapErrorCode(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("stale_ref")]
    [InlineData("STALE_REF ")]
    [InlineData("A_CODE_A_NEWER_LIBRARY_ADDED")]
    [InlineData("desktop_self_target")]
    [InlineData("SelfTarget")]
    [InlineData("EngineFaulted")]
    public void A_code_Asura_does_not_know_is_a_plain_failed_action(string? code)
    {
        Assert.Equal(AgentDesktopFailureCode.ActionFailed, AgentDesktopEnvelope.MapErrorCode(code));
    }

    [Fact]
    public void The_internal_code_has_no_mapping_of_its_own_because_the_session_faults_the_run_first()
    {
        Assert.Equal("INTERNAL", AgentDesktopEnvelope.InternalCode);
        Assert.Equal(AgentDesktopFailureCode.ActionFailed, AgentDesktopEnvelope.MapErrorCode(AgentDesktopEnvelope.InternalCode));
    }

    [Theory]
    [InlineData(-1, "PERM_DENIED")]
    [InlineData(-2, "ELEMENT_NOT_FOUND")]
    [InlineData(-3, "APP_NOT_FOUND")]
    [InlineData(-4, "ACTION_FAILED")]
    [InlineData(-5, "ACTION_NOT_SUPPORTED")]
    [InlineData(-6, "STALE_REF")]
    [InlineData(-7, "WINDOW_NOT_FOUND")]
    [InlineData(-8, "PLATFORM_NOT_SUPPORTED")]
    [InlineData(-9, "TIMEOUT")]
    [InlineData(-10, "INVALID_ARGS")]
    [InlineData(-11, "NOTIFICATION_NOT_FOUND")]
    [InlineData(-12, "INTERNAL")]
    [InlineData(-13, "SNAPSHOT_NOT_FOUND")]
    [InlineData(-14, "POLICY_DENIED")]
    [InlineData(-15, "AMBIGUOUS_TARGET")]
    [InlineData(-16, "APP_UNRESPONSIVE")]
    public void Every_AdResult_in_the_header_has_its_upstream_code(int status, string expected)
    {
        // The numbers are AD_RESULT_ERR_* in agent_desktop.h, ABI major 7.
        Assert.Equal(expected, AgentDesktopEnvelope.StatusCode(status));
    }

    [Theory]
    [InlineData(-17)]
    [InlineData(-1000)]
    [InlineData(int.MinValue)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void A_status_a_newer_library_added_is_a_plain_failed_action(int status)
    {
        Assert.Equal("ACTION_FAILED", AgentDesktopEnvelope.StatusCode(status));
        Assert.Equal(
            AgentDesktopFailureCode.ActionFailed,
            AgentDesktopEnvelope.MapErrorCode(AgentDesktopEnvelope.StatusCode(status)));
    }

    private static AgentDesktopEnvelope Parse(string json) => AgentDesktopEnvelope.Parse(Encoding.UTF8.GetBytes(json));

    /// <summary>An object nested <paramref name="levels"/> deep, itself one level.</summary>
    private static string Nested(int levels) =>
        string.Concat(Enumerable.Repeat("{\"a\":", levels - 1)) + "{}" + new string('}', levels - 1);
}
