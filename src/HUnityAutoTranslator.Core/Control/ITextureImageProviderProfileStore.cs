using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Control;

public interface ITextureImageProviderProfileStore
{
    IReadOnlyList<TextureImageProviderProfileDefinition> LoadAll();

    void Save(TextureImageProviderProfileDefinition profile);

    bool Delete(string id);

    string Export(string id);

    TextureImageProviderProfileDefinition Import(string content, IReadOnlyCollection<string> existingIds);
}
