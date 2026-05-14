namespace HUnityAutoTranslator.Core.Glossary;

/// <summary>
/// 自动术语备注的固定分类。AI 提取时被要求只能从这 9 类里选，
/// 解析结果再统一过 <see cref="Normalize"/> 兜底，保证入库的备注必然规范、可分组筛选。
/// </summary>
public static class GlossaryTermCategory
{
    public const string CharacterName = "角色名";
    public const string BossOrEnemyName = "Boss·敌人名";
    public const string PlaceName = "地名";
    public const string ItemName = "物品名";
    public const string SkillName = "技能名";
    public const string FactionName = "阵营·组织名";
    public const string UiText = "UI文本";
    public const string WorldTerm = "世界观术语";
    public const string Other = "其他";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        CharacterName, BossOrEnemyName, PlaceName, ItemName,
        SkillName, FactionName, UiText, WorldTerm, Other,
    };

    private static readonly HashSet<string> Canonical = new(All, StringComparer.Ordinal);

    // 常见变体（不区分大小写整体匹配）→ 规范分类。覆盖 AI 不守规矩时的多语言/多写法输出。
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["character"] = CharacterName, ["character name"] = CharacterName, ["char"] = CharacterName,
        ["npc"] = CharacterName, ["npc name"] = CharacterName, ["person"] = CharacterName, ["name"] = CharacterName,
        ["角色"] = CharacterName, ["人名"] = CharacterName, ["人物"] = CharacterName, ["人物名"] = CharacterName,
        ["キャラ"] = CharacterName, ["キャラクター"] = CharacterName,

        ["boss"] = BossOrEnemyName, ["boss name"] = BossOrEnemyName, ["boss encounter name"] = BossOrEnemyName,
        ["enemy"] = BossOrEnemyName, ["enemy name"] = BossOrEnemyName, ["monster"] = BossOrEnemyName,
        ["monster name"] = BossOrEnemyName, ["mob"] = BossOrEnemyName,
        ["boss名"] = BossOrEnemyName, ["敌人"] = BossOrEnemyName, ["敌人名"] = BossOrEnemyName,
        ["怪物"] = BossOrEnemyName, ["怪物名"] = BossOrEnemyName, ["敌方"] = BossOrEnemyName,

        ["place"] = PlaceName, ["place name"] = PlaceName, ["location"] = PlaceName,
        ["location name"] = PlaceName, ["area"] = PlaceName, ["region"] = PlaceName,
        ["地点"] = PlaceName, ["地区"] = PlaceName, ["区域"] = PlaceName, ["场景名"] = PlaceName,

        ["item"] = ItemName, ["item name"] = ItemName, ["weapon"] = ItemName, ["weapon name"] = ItemName,
        ["equipment"] = ItemName, ["gear"] = ItemName, ["consumable"] = ItemName, ["loot"] = ItemName,
        ["物品"] = ItemName, ["道具"] = ItemName, ["武器"] = ItemName, ["装备"] = ItemName, ["物品名称"] = ItemName,

        ["skill"] = SkillName, ["skill name"] = SkillName, ["ability"] = SkillName, ["ability name"] = SkillName,
        ["spell"] = SkillName, ["magic"] = SkillName, ["technique"] = SkillName,
        ["技能"] = SkillName, ["能力"] = SkillName, ["招式"] = SkillName, ["法术"] = SkillName, ["魔法"] = SkillName,

        ["faction"] = FactionName, ["faction name"] = FactionName, ["organization"] = FactionName,
        ["organisation"] = FactionName, ["guild"] = FactionName, ["clan"] = FactionName, ["group"] = FactionName,
        ["阵营"] = FactionName, ["组织"] = FactionName, ["势力"] = FactionName, ["公会"] = FactionName, ["团体"] = FactionName,

        ["ui"] = UiText, ["ui text"] = UiText, ["ui label"] = UiText, ["ui term"] = UiText, ["label"] = UiText,
        ["button"] = UiText, ["menu"] = UiText, ["menu text"] = UiText, ["interface"] = UiText,
        ["界面"] = UiText, ["按钮"] = UiText, ["菜单"] = UiText, ["ui术语"] = UiText, ["界面文本"] = UiText,

        ["world term"] = WorldTerm, ["lore"] = WorldTerm, ["terminology"] = WorldTerm, ["term"] = WorldTerm,
        ["concept"] = WorldTerm, ["proper noun"] = WorldTerm, ["proper nouns"] = WorldTerm,
        ["世界观"] = WorldTerm, ["术语"] = WorldTerm, ["专有名词"] = WorldTerm, ["设定"] = WorldTerm,

        ["other"] = Other, ["misc"] = Other, ["miscellaneous"] = Other, ["unknown"] = Other,
        ["其它"] = Other, ["未知"] = Other,
    };

    // 关键词启发式：变体词典没整体命中时，按子串关键词兜底。数组顺序即优先级。
    private static readonly (string Keyword, string Category)[] Keywords =
    {
        ("boss", BossOrEnemyName), ("enemy", BossOrEnemyName), ("monster", BossOrEnemyName),
        ("敌", BossOrEnemyName), ("怪", BossOrEnemyName),
        ("character", CharacterName), ("npc", CharacterName), ("角色", CharacterName),
        ("人名", CharacterName), ("人物", CharacterName), ("キャラ", CharacterName),
        ("place", PlaceName), ("location", PlaceName), ("地名", PlaceName), ("地点", PlaceName),
        ("地区", PlaceName), ("区域", PlaceName),
        ("weapon", ItemName), ("item", ItemName), ("equipment", ItemName), ("物品", ItemName),
        ("道具", ItemName), ("武器", ItemName), ("装备", ItemName),
        ("skill", SkillName), ("ability", SkillName), ("spell", SkillName), ("magic", SkillName),
        ("技能", SkillName), ("能力", SkillName), ("招式", SkillName), ("法术", SkillName), ("魔法", SkillName),
        ("faction", FactionName), ("organization", FactionName), ("guild", FactionName),
        ("阵营", FactionName), ("组织", FactionName), ("势力", FactionName), ("公会", FactionName),
        ("button", UiText), ("menu", UiText), ("label", UiText), ("interface", UiText),
        ("界面", UiText), ("按钮", UiText), ("菜单", UiText),
        ("lore", WorldTerm), ("terminology", WorldTerm), ("concept", WorldTerm), ("proper noun", WorldTerm),
        ("世界观", WorldTerm), ("术语", WorldTerm), ("专有名词", WorldTerm), ("设定", WorldTerm),
    };

    /// <summary>该值是否已经是 9 类规范分类之一。</summary>
    public static bool IsCanonical(string? value)
    {
        return value != null && Canonical.Contains(value);
    }

    /// <summary>把任意备注文本归一化到 9 类规范分类之一；无法识别一律归入 <see cref="Other"/>。</summary>
    public static string Normalize(string? rawNote)
    {
        var trimmed = (rawNote ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return Other;
        }

        if (Canonical.Contains(trimmed))
        {
            return trimmed;
        }

        if (Aliases.TryGetValue(trimmed, out var aliased))
        {
            return aliased;
        }

        var lower = trimmed.ToLowerInvariant();
        foreach (var (keyword, category) in Keywords)
        {
            if (lower.IndexOf(keyword, StringComparison.Ordinal) >= 0)
            {
                return category;
            }
        }

        return Other;
    }
}
