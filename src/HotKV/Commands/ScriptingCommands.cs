// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.Commands
{
    /// <summary>Typed wrappers for Lua scripting commands.</summary>
    public static class ScriptingCommands
    {
        private static List<object?> BuildEvalArgs(string command, string scriptOrSha, IReadOnlyList<string> keys, IReadOnlyList<string> args)
        {
            var tokens = new List<object?>(keys.Count + args.Count + 3) { command, scriptOrSha, keys.Count };
            tokens.AddRange(keys);
            tokens.AddRange(args);
            return tokens;
        }

        /// <summary>EVAL script numkeys key [key ...] arg [arg ...]. Returns the raw reply: scripts can return any RESP shape.</summary>
        public static ValueTask<RespValue> EvalAsync(this ICommandExecutor executor, string script, IReadOnlyList<string> keys, IReadOnlyList<string> args, CancellationToken cancellationToken = default) =>
            executor.ExecuteAsync(BuildEvalArgs("EVAL", script, keys, args), cancellationToken);

        /// <summary>EVALSHA sha1 numkeys key [key ...] arg [arg ...]. Throws a server exception with prefix "NOSCRIPT" if the script is not cached; see <see cref="EvalShaOrEvalAsync"/> to fall back automatically.</summary>
        public static ValueTask<RespValue> EvalShaAsync(this ICommandExecutor executor, string sha1, IReadOnlyList<string> keys, IReadOnlyList<string> args, CancellationToken cancellationToken = default) =>
            executor.ExecuteAsync(BuildEvalArgs("EVALSHA", sha1, keys, args), cancellationToken);

        /// <summary>SCRIPT LOAD script -> its SHA-1, for later EVALSHA calls.</summary>
        public static async Task<string> ScriptLoadAsync(this ICommandExecutor executor, string script, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "SCRIPT", "LOAD", script }, cancellationToken).ConfigureAwait(false);
            return reply.AsString() ?? string.Empty;
        }

        /// <summary>SCRIPT EXISTS sha1 [sha1 ...] -> whether each script is cached, in the same order.</summary>
        public static async Task<IReadOnlyList<bool>> ScriptExistsAsync(this ICommandExecutor executor, IReadOnlyList<string> shas, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(shas.Count + 2) { "SCRIPT", "EXISTS" };
            args.AddRange(shas);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            var result = new List<bool>(shas.Count);
            foreach (var item in reply.AsArray())
            {
                result.Add(item.AsInt64() == 1);
            }
            return result;
        }

        /// <summary>SCRIPT FLUSH.</summary>
        public static Task ScriptFlushAsync(this ICommandExecutor executor, CancellationToken cancellationToken = default) =>
            executor.ExecuteAsync(new object?[] { "SCRIPT", "FLUSH" }, cancellationToken).AsTask();

        /// <summary>Computes the lowercase hex SHA-1 EVALSHA uses to identify a script.</summary>
        public static string ComputeScriptSha1(string script)
        {
#if NET8_0_OR_GREATER
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(script));
#else
            using var sha1 = SHA1.Create();
            byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(script));
#endif
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Runs EVALSHA against the script's own SHA-1, transparently
        /// falling back to EVAL (which also primes the script cache for
        /// next time) when the server replies NOSCRIPT.
        /// </summary>
        public static async Task<RespValue> EvalShaOrEvalAsync(this ICommandExecutor executor, string script, IReadOnlyList<string> keys, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
        {
            string sha1 = ComputeScriptSha1(script);
            try
            {
                return await executor.EvalShaAsync(sha1, keys, args, cancellationToken).ConfigureAwait(false);
            }
            catch (HotKVServerException ex) when (ex.Prefix == "NOSCRIPT")
            {
                return await executor.EvalAsync(script, keys, args, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
