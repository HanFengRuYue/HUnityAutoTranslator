namespace HUnityAutoTranslator.Core.Glossary;

/// <summary>
/// 判定术语「源词」是否为不该进术语库的简短高频功能词/感叹词。
/// 这类词在不同上下文里译法本就不同，固定成术语会误导 AI 并触发无意义的质量校验失败。
/// 提取端过滤与存量清理共用同一套规则。
/// </summary>
public static class SuspiciousGlossaryTermDetector
{
    // 各源语言的高频功能词/感叹词/应答词，合并为一个全局集合：
    // 不同语言词形基本不冲突，分语言分组只为可读性与维护。
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        // 日语
        "はい", "いいえ", "いえ", "ええ", "うん", "ううん", "そう", "そうだ", "そうです",
        "もう", "まだ", "また", "です", "ます", "でも", "けど", "から", "ので", "のに",
        "だめ", "やった", "よし", "さあ", "ほら", "ねえ", "あの", "その", "この",
        "ありがとう", "ありがとうございます", "すみません", "ごめん", "ごめんなさい",
        "おはよう", "こんにちは", "こんばんは", "さようなら", "おやすみ",
        "わかった", "わかりました", "なに", "なん", "だれ", "どこ", "いつ", "なぜ", "どう",
        // 英语
        "yes", "no", "ok", "okay", "yeah", "yep", "nope", "sure", "well", "oh", "ah",
        "hi", "hey", "hello", "bye", "thanks", "thank you", "sorry", "please",
        "back", "next", "done", "cancel", "close", "open", "start", "continue", "exit",
        "quit", "save", "load", "menu", "play", "pause", "resume", "retry", "skip",
        "the", "and", "or", "but", "this", "that", "what", "who", "where", "when", "why", "how",
        // 韩语
        "예", "아니요", "아니", "네", "응", "그래", "그럼", "음",
        "감사합니다", "고맙습니다", "죄송합니다", "미안", "안녕",
    };

    /// <summary>源词命中停用词黑名单，或被脚本启发式判为功能词，即视为可疑。</summary>
    public static bool IsSuspicious(string? sourceTerm)
    {
        var trimmed = (sourceTerm ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        return Stopwords.Contains(trimmed) || LooksLikeFunctionWord(trimmed);
    }

    /// <summary>
    /// 纯假名/纯谚文且很短的源词几乎必然是功能词/语气词，
    /// 而专有名词、物品名、技能名通常带汉字或片假名。
    /// </summary>
    public static bool LooksLikeFunctionWord(string? sourceTerm)
    {
        var trimmed = (sourceTerm ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > 3)
        {
            return false;
        }

        // 纯平假名（含长音符 ー）且 ≤3 字符：はい / いいえ / うん / そう / です …
        if (trimmed.All(IsHiragana))
        {
            return true;
        }

        // 纯谚文且 ≤2 字符：예 / 네 / 응 …（谚文是韩语唯一文字，门槛收得更紧）
        if (trimmed.Length <= 2 && trimmed.All(IsHangul))
        {
            return true;
        }

        return false;
    }

    private static bool IsHiragana(char ch)
    {
        // 平假名块 U+3040-U+309F + 长音符 ー U+30FC
        return (ch >= '぀' && ch <= 'ゟ') || ch == 'ー';
    }

    private static bool IsHangul(char ch)
    {
        // 谚文音节 + 字母 + 兼容字母（区段同 TextFilter.ContainsKanaOrHangul）
        return (ch >= '가' && ch <= '힯')
            || (ch >= 'ᄀ' && ch <= 'ᇿ')
            || (ch >= '㄰' && ch <= '㆏');
    }
}
