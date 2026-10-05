import java.nio.charset.StandardCharsets;
import java.util.Base64;
import javax.crypto.Cipher;
import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.PBEKeySpec;
import javax.crypto.spec.SecretKeySpec;

/**
 * One-shot helper: decrypt the built-in AI key that ships inside the Android app
 * (AnkiAssistantAndroid/src/com/ankiassistant/Secret.java) and print it, so the same
 * key can be sealed into the Windows build.
 *
 *   java tools/Unseal.java            -> prints key + its length
 *   java tools/Unseal.java --wire     -> prints a line that MakeSecret (C#) can consume
 *
 * Pure ASCII on purpose (Windows PowerShell 5.1 scratch encoding).
 */
public class Unseal {

    // copied verbatim from AnkiAssistantAndroid/src/com/ankiassistant/Secret.java
    static final String SALT_B64 = "ynUB5A8f3kztyZX/iovgtA==";
    static final String IV_B64   = "ppd6+/q3d3JVowSB";
    static final String CT_B64   =
        "rrSaxmxkitwgZDu7COYjmMnHfBDS0dIqMrk7gqXcKUss4jJDVl84/Otc95nUK8QjeiRCC+K6FlCOoehbyLy07xY=";
    static final int ITER = 12000;
    static final int KEY_BITS = 256;

    static String pass() {
        String r = "72XR9jbChp0zqU";
        String p1 = new StringBuilder(r).reverse().toString();
        char[] c = {'Q', 'P', 'D', 'A', 'S', 'r', 'E', '4'};
        return "t6duyvxf" + p1 + "msLNBeWlIH" + new String(c);
    }

    public static void main(String[] args) throws Exception {
        byte[] salt = Base64.getDecoder().decode(SALT_B64);
        byte[] iv = Base64.getDecoder().decode(IV_B64);
        byte[] ct = Base64.getDecoder().decode(CT_B64);
        String p = pass();
        System.out.println("pass length = " + p.length());
        byte[] key = SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256")
            .generateSecret(new PBEKeySpec(p.toCharArray(), salt, ITER, KEY_BITS))
            .getEncoded();
        Cipher c = Cipher.getInstance("AES/GCM/NoPadding");
        c.init(Cipher.DECRYPT_MODE, new SecretKeySpec(key, "AES"), new GCMParameterSpec(128, iv));
        String plain = new String(c.doFinal(ct), StandardCharsets.UTF_8);
        System.out.println("key length = " + plain.length());
        System.out.println("KEY = " + plain);
    }
}
