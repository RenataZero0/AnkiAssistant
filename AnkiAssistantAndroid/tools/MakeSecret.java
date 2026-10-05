import javax.crypto.Cipher;
import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.PBEKeySpec;
import javax.crypto.spec.SecretKeySpec;
import java.security.SecureRandom;
import java.util.Base64;

/**
 * 生成 Secret.java 里要填的密文（跟 App 运行时用的是同一套算法：
 * PBKDF2WithHmacSHA256 派生密钥 → AES/GCM/NoPadding 加密）。
 *
 * 用法：
 *   java tools/MakeSecret.java <明文Key> <口令>
 *
 * 输出可以直接粘进 src/com/ankiassistant/Secret.java。
 * 口令本身不会进这个文件，只会以拆散的形式出现在 Secret.java 里。
 */
public class MakeSecret {

    static final int ITER = 12000;
    static final int KEY_BITS = 256;

    public static void main(String[] args) throws Exception {
        if (args.length < 2) {
            System.out.println("用法: java tools/MakeSecret.java <明文Key> <口令>");
            return;
        }
        String plain = args[0];
        String pass = args[1];

        byte[] salt = new byte[16];
        byte[] iv = new byte[12];
        SecureRandom rnd = new SecureRandom();
        rnd.nextBytes(salt);
        rnd.nextBytes(iv);

        byte[] key = derive(pass, salt);
        Cipher c = Cipher.getInstance("AES/GCM/NoPadding");
        c.init(Cipher.ENCRYPT_MODE, new SecretKeySpec(key, "AES"), new GCMParameterSpec(128, iv));
        byte[] ct = c.doFinal(plain.getBytes("UTF-8"));

        Base64.Encoder b64 = Base64.getEncoder();
        System.out.println("// ==== 粘进 Secret.java（下面四行替换掉同名字段）====");
        System.out.println("    private static final String SALT_B64 = \"" + b64.encodeToString(salt) + "\";");
        System.out.println("    private static final String IV_B64   = \"" + b64.encodeToString(iv) + "\";");
        System.out.println("    private static final String CT_B64   = \"" + b64.encodeToString(ct) + "\";");
        System.out.println("// 明文长度 " + plain.length() + "，密文 " + ct.length + " 字节");
        System.out.println();
        System.out.println("// 自检用：");
        System.out.println("// java tools/MakeSecret.java --verify " + b64.encodeToString(salt) + " "
                + b64.encodeToString(iv) + " " + b64.encodeToString(ct) + " " + pass);
    }

    static byte[] derive(String pass, byte[] salt) throws Exception {
        SecretKeyFactory f = SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256");
        return f.generateSecret(new PBEKeySpec(pass.toCharArray(), salt, ITER, KEY_BITS)).getEncoded();
    }
}
