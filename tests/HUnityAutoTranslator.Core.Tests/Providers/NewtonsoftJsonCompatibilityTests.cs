using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using FluentAssertions;
using HUnityAutoTranslator.Core.Providers;
using HUnityAutoTranslator.Core.Textures;

namespace HUnityAutoTranslator.Core.Tests.Providers;

public sealed class NewtonsoftJsonCompatibilityTests
{
    private static readonly OpCode[] SingleByteOpCodes = new OpCode[0x100];
    private static readonly OpCode[] MultiByteOpCodes = new OpCode[0x100];

    static NewtonsoftJsonCompatibilityTests()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
            {
                continue;
            }

            var value = (ushort)opCode.Value;
            if (value < 0x100)
            {
                SingleByteOpCodes[value] = opCode;
            }
            else if ((value & 0xff00) == 0xfe00)
            {
                MultiByteOpCodes[value & 0xff] = opCode;
            }
        }
    }

    [Theory]
    [InlineData(typeof(OpenAiResponsesProvider))]
    [InlineData(typeof(ChatCompletionsProvider))]
    public void Provider_request_creation_avoids_jtoken_to_string_formatting_overload(Type providerType)
    {
        var createRequest = providerType.GetMethod("CreateRequest", BindingFlags.Instance | BindingFlags.NonPublic);

        createRequest.Should().NotBeNull();
        GetCalledMethods(createRequest!)
            .Any(IsJTokenToStringFormattingCall)
            .Should()
            .BeFalse("Unity games can preload Newtonsoft.Json 12.x, which lacks this Newtonsoft.Json 13.x overload");
    }

    [Fact]
    public void Openai_compatible_extra_body_normalization_avoids_jtoken_to_string_formatting_overload()
    {
        var normalize = typeof(OpenAICompatibleRequestOptions).GetMethod(
            nameof(OpenAICompatibleRequestOptions.NormalizeExtraBodyJson),
            BindingFlags.Static | BindingFlags.Public);

        normalize.Should().NotBeNull();
        GetCalledMethods(normalize!)
            .Any(IsJTokenToStringFormattingCall)
            .Should()
            .BeFalse("Unity games can preload Newtonsoft.Json 12.x, which lacks this Newtonsoft.Json 13.x overload");
    }

    [Fact]
    public void Texture_vision_detection_request_creation_avoids_jtoken_to_string_formatting_overload()
    {
        var detect = typeof(TextureVisionTextClient).GetMethod(nameof(TextureVisionTextClient.DetectAsync));

        detect.Should().NotBeNull();
        GetCalledMethodsIncludingAsyncStateMachine(detect!)
            .Any(IsJTokenToStringFormattingCall)
            .Should()
            .BeFalse("Unity games can preload Newtonsoft.Json 12.x, which lacks this Newtonsoft.Json 13.x overload");
    }

    private static bool IsJTokenToStringFormattingCall(MethodBase method)
    {
        var parameters = method.GetParameters();
        return method.DeclaringType?.FullName == "Newtonsoft.Json.Linq.JToken"
            && method.Name == "ToString"
            && parameters.Length == 1
            && parameters[0].ParameterType.FullName == "Newtonsoft.Json.Formatting";
    }

    private static IEnumerable<MethodBase> GetCalledMethods(MethodInfo method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var module = method.Module;
        var typeArguments = method.DeclaringType?.GetGenericArguments();
        var methodArguments = method.GetGenericArguments();
        var index = 0;
        while (index < il.Length)
        {
            var opCode = ReadOpCode(il, ref index);
            if (opCode.OperandType == OperandType.InlineMethod)
            {
                var token = BitConverter.ToInt32(il, index);
                index += 4;
                var resolvedMethod = module.ResolveMethod(token, typeArguments, methodArguments);
                if (resolvedMethod is not null)
                {
                    yield return resolvedMethod;
                }
                continue;
            }

            SkipOperand(il, ref index, opCode.OperandType);
        }
    }

    private static IEnumerable<MethodBase> GetCalledMethodsIncludingAsyncStateMachine(MethodInfo method)
    {
        foreach (var calledMethod in GetCalledMethods(method))
        {
            yield return calledMethod;
        }

        var stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        var moveNext = stateMachine?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (moveNext == null)
        {
            yield break;
        }

        foreach (var calledMethod in GetCalledMethods(moveNext))
        {
            yield return calledMethod;
        }
    }

    private static OpCode ReadOpCode(byte[] il, ref int index)
    {
        var value = il[index++];
        if (value == 0xfe)
        {
            return MultiByteOpCodes[il[index++]];
        }

        return SingleByteOpCodes[value];
    }

    private static void SkipOperand(byte[] il, ref int index, OperandType operandType)
    {
        index += operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget => 1,
            OperandType.ShortInlineI => 1,
            OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget => 4,
            OperandType.InlineField => 4,
            OperandType.InlineI => 4,
            OperandType.InlineSig => 4,
            OperandType.InlineString => 4,
            OperandType.InlineTok => 4,
            OperandType.InlineType => 4,
            OperandType.ShortInlineR => 4,
            OperandType.InlineI8 => 8,
            OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, index) * 4,
            _ => throw new NotSupportedException($"Unsupported IL operand type: {operandType}")
        };
    }
}
