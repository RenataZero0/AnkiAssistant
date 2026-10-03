package com.ankiassistant;

import android.util.Base64;

import java.nio.charset.Charset;

import javax.crypto.Cipher;
import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.PBEKeySpec;
import javax.crypto.spec.SecretKeySpec;

/**
 * 内置的 AI Key（密文形式）。
 *
 * 目标：**新设备装完就能用**，不用再手输 Key。
 *
 * 做法：
 *   * APK 里只有 AES-GCM 密文（{@link #CT_B64}），没有明文；明文只在运行时于内存里出现一小会儿
 *   * 解密口令不写成完整字符串，而是拆成 4 段、分散在不同方法里，
 *     其中一段还反序存放（见 {@link #p1()}）—— 直接 strings 扫 APK 拿不到口令
 *   * 口令 + 随机 salt 走 PBKDF2WithHmacSHA256（12000 轮）派生出 AES-256 密钥，再解 GCM 密文
 *
 * 说清楚边界：**只要密钥随 APK 分发，就一定能被逆向出来**，加密只是把"随手提取"提高到"需要真正逆向"。
 * 所以另外两条才是真正的防线：
 *   1. 设置里手填的 Key **优先于**内置 Key（{@link Store#aiApiKeyFor}）—— 发现异常随时换一把
 *   2. 换 Key 只需要一条命令重新生成密文：`java tools/MakeSecret.java <新Key> <新口令>`（见 README）
 *
 * 开发约定：本文件不要提交明文 Key，也不要提交口令的完整串。
 */
public class Secret {

    /** 生成方式：java tools/MakeSecret.java "de76…sum" "<40 位口令>" */
    private static final String SALT_B64 = "ynUB5A8f3kztyZX/iovgtA==";
    private static final String IV_B64   = "ppd6+/q3d3JVowSB";
    private static final String CT_B64   =
            "rrSaxmxkitwgZDu7COYjmMnHfBDS0dIqMrk7gqXcKUss4jJDVl84/Otc95nUK8QjeiRCC+K6FlCOoehbyLy07xY=";

    private static final int ITER = 12000;
    private static final int KEY_BITS = 256;

    /** 解出来的 Key 缓存（只在本进程内存里） */
    private static String cached;

    /**
     * 某个服务商的内置 Key；没有内置就返回空串。
     * 只有智谱（免费档）内置 —— 付费余额的 Key（如 DeepSeek）故意不打包，泄露代价更高。
     */
    public static String defaultKey(String provider) {
        if (AiClient.P_ZHIPU.equals(provider)) return zhipu();
        return "";
    }

    /** 智谱 GLM 的内置 Key（解不出来就返回空串，设置里手填即可） */
    public static String zhipu() {
        if (cached != null) return cached;
        cached = decrypt(assemblePass(), SALT_B64, IV_B64, CT_B64);
        return cached;
    }

    // ------------------------------------------------------------ 口令拼装

    /** 口令 = 4 段拼起来，共 40 位；每段放在不同方法里，避免被一次性读出来 */
    private static String assemblePass() {
        StringBuilder sb = new StringBuilder(40);
        sb.append("t6duyvxf");          // 1/4
        sb.append(p1());                // 2/4（反序存放）
        sb.append("msLNBeWlIH");        // 3/4
        sb.append(p2());                // 4/4（字符数组形式）
        return sb.toString();
    }

    private static String p1() {
        // 反着存的，运行时倒回来
        String r = "72XR9jbChp0zqU";
        return new StringBuilder(r).reverse().toString();
    }

    private static String p2() {
        char[] c = {'Q', 'P', 'D', 'A', 'S', 'r', 'E', '4'};
        return new String(c);
    }

    // ------------------------------------------------------------ 解密

    static String decrypt(String pass, String saltB64, String ivB64, String ctB64) {
        try {
            byte[] salt = Base64.decode(saltB64, Base64.DEFAULT);
            byte[] iv = Base64.decode(ivB64, Base64.DEFAULT);
            byte[] ct = Base64.decode(ctB64, Base64.DEFAULT);

            SecretKeyFactory f = SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256");
            byte[] key = f.generateSecret(new PBEKeySpec(pass.toCharArray(), salt, ITER, KEY_BITS))
                    .getEncoded();

            Cipher c = Cipher.getInstance("AES/GCM/NoPadding");
            c.init(Cipher.DECRYPT_MODE, new SecretKeySpec(key, "AES"), new GCMParameterSpec(128, iv));
            byte[] plain = c.doFinal(ct);
            return new String(plain, Charset.forName("UTF-8"));
        } catch (Throwable t) {
            // 解不出来不是致命问题：设置里手填 Key 照样能用
            android.util.Log.w("AnkiAssistant", "内置 Key 解密失败（可在设置里手填）：" + t);
            return "";
        }
    }
}
