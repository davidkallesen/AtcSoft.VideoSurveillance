namespace Linksoft.VideoSurveillance.Api.Domain.ApiHandlers.Settings;

/// <summary>
/// Handler business logic for the UpdateSettings operation.
/// </summary>
public sealed class UpdateSettingsHandler(
    IApplicationSettingsService settingsService) : IUpdateSettingsHandler
{
    public Task<UpdateSettingsResult> ExecuteAsync(
        UpdateSettingsParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var general = settingsService.General;
        var cameraDisplay = settingsService.CameraDisplay;
        var connection = settingsService.Connection;
        var performance = settingsService.Performance;
        var motionDetection = settingsService.MotionDetection;
        var recording = settingsService.Recording;
        var advanced = settingsService.Advanced;

        parameters.Request.ApplyToCore(
            general,
            cameraDisplay,
            connection,
            performance,
            motionDetection,
            recording,
            advanced);

        settingsService.SaveGeneral(general);
        settingsService.SaveCameraDisplay(cameraDisplay);
        settingsService.SaveConnection(connection);
        settingsService.SavePerformance(performance);
        settingsService.SaveMotionDetection(motionDetection);
        settingsService.SaveRecording(recording);
        settingsService.SaveAdvanced(advanced);

        var updated = SettingsMappingExtensions.ToApiModel(
            settingsService.General,
            settingsService.CameraDisplay,
            settingsService.Connection,
            settingsService.Performance,
            settingsService.MotionDetection,
            settingsService.Recording,
            settingsService.Advanced);

        return Task.FromResult(UpdateSettingsResult.Ok(updated));
    }
}