using System;
using System.Collections.Generic;

namespace CtxTray.Core
{
    /// <summary>
    /// セッションがいま何をしているか。Claude Code が ~/.claude/sessions/&lt;pid&gt;.json に書く
    /// status と waitingFor から決める。
    ///
    /// 確かめた値（Desktop の Code タブ 2.1.286、ターミナル 2.1.283。--watch-status で記録）:
    ///   busy                                応答を作っている
    ///   waiting + waitingFor "permission prompt"  ツールの承認待ち
    ///   waiting + waitingFor "input needed"       質問（AskUserQuestion）の回答待ち
    ///   idle                                応答が終わった
    /// status の無い版、知らない値は Unknown（何も出さない。推測で埋めない）。
    /// waiting で waitingFor が知らない文なら Waiting（待っていることだけは確か）。
    ///
    /// Unread・UnreadNeedsAction は Desktop のタブだけ。応答が終わった後にそのタブを開いていない（WithUnread）。
    /// Desktop のサイドバーでは、最後の応答の分類が completed なら青、blocked（利用者の対応が要る。
    /// 応答が質問で終わったときなど）ならアンバーの丸になり、どちらもタブを開くと消える（2026-10-03 実測）。
    /// </summary>
    internal enum SessionActivity
    {
        Unknown,
        Idle,
        Busy,
        Waiting,
        WaitingPermission,
        WaitingInput,
        Unread,
        UnreadNeedsAction,
    }

    internal static class SessionActivities
    {
        public static SessionActivity Parse(string status, string waitingFor)
        {
            if (string.Equals(status, "busy", StringComparison.Ordinal)) return SessionActivity.Busy;
            if (string.Equals(status, "idle", StringComparison.Ordinal)) return SessionActivity.Idle;
            if (!string.Equals(status, "waiting", StringComparison.Ordinal)) return SessionActivity.Unknown;

            if (string.Equals(waitingFor, "permission prompt", StringComparison.Ordinal))
                return SessionActivity.WaitingPermission;
            if (string.Equals(waitingFor, "input needed", StringComparison.Ordinal))
                return SessionActivity.WaitingInput;
            return SessionActivity.Waiting;
        }

        /// <summary>
        /// Desktop のタブの「未読」を足す。Desktop のサイドバーの青い丸は「応答が終わったとき、そのタブを
        /// 見ていなかった」ことを表し、タブを開くと消える。タブの記録には「最後に開いた時刻」（lastFocusedAt）
        /// しか無いので、次のように読み替える。
        ///
        ///   終わっていて（idle）、最後の応答（transcript の応答の時刻）がそのタブを最後に開いた時刻より後で、
        ///   その間に別のタブが開かれている（＝応答が来たとき、見ていたのは別のタブ）なら Unread。
        ///
        /// ★ 別のタブが開かれたかまで見る。「最後に開いた時刻より後に応答」だけだと、見ているタブで応答が
        ///   終わってから別のタブへ移ったときも未読になる（離れた時刻は記録されない）。
        /// ★ idle になった時刻（statusUpdatedAt）では比べない。古いタブを開くとプロセスが起動して、開いた直後に
        ///   idle を書くので、応答が無くても未読に見えてしまった（2026-10-03 実測）。
        /// ★ 記録は「最後に開いた時刻」だけなので、応答の前後に同じ別のタブを 2 回開くと前の 1 回が消え、
        ///   未読なのに既読と判定することがある（出しすぎるより、出し損ねる側に倒れる）。
        ///   lastFocusedAt は開いてから 2〜4 秒遅れて書かれるので、切り替えた直後はその間だけ食い違うことがある。
        /// </summary>
        /// <param name="otherFocusedMs">ほかのタブの lastFocusedAt。</param>
        /// <param name="turnCategory">最後の応答の分類（postTurnSummary.status_category）。blocked なら UnreadNeedsAction。</param>
        public static SessionActivity WithUnread(SessionActivity a, long lastReplyMs, long lastFocusedMs,
                                                 IEnumerable<long> otherFocusedMs, string turnCategory = null)
        {
            if (a != SessionActivity.Idle || lastReplyMs <= 0 || lastReplyMs <= lastFocusedMs) return a;
            if (otherFocusedMs == null) return a;
            foreach (var other in otherFocusedMs)
            {
                if (other > lastFocusedMs && other < lastReplyMs)
                    return string.Equals(turnCategory, "blocked", StringComparison.Ordinal)
                        ? SessionActivity.UnreadNeedsAction : SessionActivity.Unread;
            }
            return a;
        }

        /// <summary>
        /// 利用者の対応を待っているか（アンバーの丸、強調の対象）。
        /// 対応が要る未読の応答も含める。Desktop が同じ色で出すため。
        /// </summary>
        public static bool IsWaiting(SessionActivity a)
        {
            return a == SessionActivity.Waiting || a == SessionActivity.WaitingPermission
                || a == SessionActivity.WaitingInput || a == SessionActivity.UnreadNeedsAction;
        }
    }
}
