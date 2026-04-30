using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Control;

public interface IProviderProfileStore
{
    IReadOnlyList<ProviderProfileDefinition> LoadAll();

    void Save(ProviderProfileDefinition profile);

    bool Delete(string id);

    string Export(string id);

    ProviderProfileDefinition Import(string content, IReadOnlyCollection<string> existingIds);
}
