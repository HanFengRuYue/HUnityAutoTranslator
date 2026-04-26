namespace HUnityAutoTranslator.Core.Control;

public interface IControlPanelSettingsStore
{
    ControlPanelSettings Load();

    void Save(ControlPanelSettings settings);
}
