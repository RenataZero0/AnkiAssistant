package com.ankiassistant;

/**
 * 版本号唯一来源。build.ps1 会解析这里的 VERSION_TAG 生成 versionName / versionCode，
 * 这样 APK 上显示的版本永远不会和代码里的版本对不上。
 *
 * 每次迭代：先在 CHANGELOG.md 顶部加一节，再把这里加一档。
 */
public class Version {
    public static final String VERSION_TAG = "v1.16.2";
    /** 纯数字版本（Changelog 里比对用） */
    public static final String VERSION_NUMBER = "1.16.2";
}
