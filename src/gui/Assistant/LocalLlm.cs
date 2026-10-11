using System;
using System.Text;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace QuantumVault
{
    /// <summary>Thin wrapper over ONNX Runtime GenAI. Streams tokens through onText.</summary>
    public sealed class LocalLlm : IDisposable
    {
        private Model _model;
        private Tokenizer _tokenizer;

        public bool IsLoaded => _model != null;

        public void Load(string dir)
        {
            Dispose();
            _model = new Model(dir);
            _tokenizer = new Tokenizer(_model);
        }

        public string Generate(string prompt, int maxNewTokens, Action<string> onText)
        {
            using var sequences = _tokenizer.Encode(prompt);
            using var generatorParams = new GeneratorParams(_model);
            generatorParams.SetSearchOption("max_length", 16384);

            using var generator = new Generator(_model, generatorParams);
            generator.AppendTokenSequences(sequences);
            using var stream = _tokenizer.CreateStream();

            var sb = new StringBuilder();
            int generated = 0;
            while (!generator.IsDone() && generated < maxNewTokens)
            {
                generator.GenerateNextToken();
                var seq = generator.GetSequence(0);
                int token = seq[seq.Length - 1];
                string piece = stream.Decode(token);

                if (piece.Contains("<|im_end|>")) break;
                generated++;
                sb.Append(piece);
                onText?.Invoke(piece);
            }
            return sb.ToString().Trim();
        }

        public void Dispose()
        {
            _tokenizer?.Dispose();
            _model?.Dispose();
            _tokenizer = null;
            _model = null;
        }
    }
}

