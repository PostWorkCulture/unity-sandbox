// Kill clips (match chat): turns a clip's JPEG frames into an H.264 .mp4 and puts it in the phone's gallery
// (Movies/imok). Frames are queued from Unity with add(), finish() starts the work on a background thread, and
// state() reports "working:<0..1>", "done:<where>" or "error:<why>". Called from KillClips.cs (ClipSaver.AndroidJob).
package com.postworkculture.streetlife;

import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.media.Image;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.provider.MediaStore;

import java.io.File;
import java.io.FileInputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.ByteBuffer;
import java.util.ArrayList;

public class ClipEncoder {
    private final Context ctx;
    private final int w, h, fps;
    private final String name;
    private final ArrayList<byte[]> frames = new ArrayList<>();
    private volatile String state = "working:0";

    public ClipEncoder(Context ctx, int w, int h, int fps, String name) {
        this.ctx = ctx.getApplicationContext();
        this.w = w & ~1; this.h = h & ~1; this.fps = Math.max(1, fps); this.name = name;
    }

    public void add(byte[] jpg) { synchronized (frames) { frames.add(jpg); } }

    public String state() { return state; }

    public void finish() {
        new Thread(new Runnable() {
            public void run() {
                try { state = "done:" + encode(); }
                catch (Throwable e) { state = "error:" + e.getMessage(); }
            }
        }, "ClipEncoder").start();
    }

    private String encode() throws Exception {
        File tmp = new File(ctx.getCacheDir(), name + ".mp4");
        MediaFormat fmt = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, w, h);
        fmt.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible);
        fmt.setInteger(MediaFormat.KEY_BIT_RATE, Math.max(2_000_000, w * h * fps / 4));
        fmt.setInteger(MediaFormat.KEY_FRAME_RATE, fps);
        fmt.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);
        fmt.setInteger(MediaFormat.KEY_COLOR_STANDARD, MediaFormat.COLOR_STANDARD_BT709);
        fmt.setInteger(MediaFormat.KEY_COLOR_RANGE, MediaFormat.COLOR_RANGE_LIMITED);
        fmt.setInteger(MediaFormat.KEY_COLOR_TRANSFER, MediaFormat.COLOR_TRANSFER_SDR_VIDEO);
        MediaCodec codec = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC);
        codec.configure(fmt, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
        codec.start();
        MediaMuxer muxer = new MediaMuxer(tmp.getAbsolutePath(), MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
        MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
        int[] track = { -1 };
        int n; synchronized (frames) { n = frames.size(); }
        int[] px = new int[w * h];
        try {
            for (int i = 0; i <= n; i++) {
                int in;
                while ((in = codec.dequeueInputBuffer(10000)) < 0) drain(codec, muxer, info, track, false);
                long pts = (long) i * 1_000_000L / fps;
                if (i == n) { codec.queueInputBuffer(in, 0, 0, pts, MediaCodec.BUFFER_FLAG_END_OF_STREAM); break; }
                byte[] jpg; synchronized (frames) { jpg = frames.get(i); }
                Bitmap bmp = BitmapFactory.decodeByteArray(jpg, 0, jpg.length);
                if (bmp.getWidth() != w || bmp.getHeight() != h) { Bitmap s = Bitmap.createScaledBitmap(bmp, w, h, true); bmp.recycle(); bmp = s; }
                bmp.getPixels(px, 0, w, 0, 0, w, h); bmp.recycle();
                Image img = codec.getInputImage(in);
                fill(img, px);
                codec.queueInputBuffer(in, 0, w * h * 3 / 2, pts, 0);
                drain(codec, muxer, info, track, false);
                state = "working:" + (0.9f * (i + 1) / n);
            }
            drain(codec, muxer, info, track, true);
        } finally {
            try { codec.stop(); } catch (Exception e) { }
            codec.release();
            try { if (track[0] >= 0) muxer.stop(); } catch (Exception e) { }
            muxer.release();
        }
        String where = publish(tmp);
        tmp.delete();
        return where;
    }

    // BT.709, limited range, into whatever plane layout the encoder hands us
    private void fill(Image img, int[] px) {
        Image.Plane[] p = img.getPlanes();
        ByteBuffer Y = p[0].getBuffer(), U = p[1].getBuffer(), V = p[2].getBuffer();
        int yr = p[0].getRowStride(), yp = p[0].getPixelStride();
        int ur = p[1].getRowStride(), up = p[1].getPixelStride();
        int vr = p[2].getRowStride(), vp = p[2].getPixelStride();
        for (int y = 0; y < h; y++) {
            int row = y * w, yo = y * yr;
            for (int x = 0; x < w; x++) {
                int c = px[row + x], r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
                Y.put(yo + x * yp, (byte) (16 + ((47 * r + 157 * g + 16 * b + 128) >> 8)));
            }
        }
        for (int y = 0; y < h / 2; y++) {
            for (int x = 0; x < w / 2; x++) {
                int i0 = (2 * y) * w + 2 * x, i1 = i0 + w;
                int c0 = px[i0], c1 = px[i0 + 1], c2 = px[i1], c3 = px[i1 + 1];
                int r = (((c0 >> 16) & 255) + ((c1 >> 16) & 255) + ((c2 >> 16) & 255) + ((c3 >> 16) & 255)) >> 2;
                int g = (((c0 >> 8) & 255) + ((c1 >> 8) & 255) + ((c2 >> 8) & 255) + ((c3 >> 8) & 255)) >> 2;
                int b = ((c0 & 255) + (c1 & 255) + (c2 & 255) + (c3 & 255)) >> 2;
                U.put(y * ur + x * up, (byte) (128 + ((-26 * r - 87 * g + 112 * b + 128) >> 8)));
                V.put(y * vr + x * vp, (byte) (128 + ((112 * r - 102 * g - 10 * b + 128) >> 8)));
            }
        }
    }

    private void drain(MediaCodec codec, MediaMuxer muxer, MediaCodec.BufferInfo info, int[] track, boolean end) {
        int idle = 0;
        while (true) {
            int o = codec.dequeueOutputBuffer(info, end ? 10000 : 0);
            if (o == MediaCodec.INFO_TRY_AGAIN_LATER) { if (!end || ++idle > 300) return; continue; }
            if (o == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) { track[0] = muxer.addTrack(codec.getOutputFormat()); muxer.start(); continue; }
            if (o < 0) continue;
            ByteBuffer buf = codec.getOutputBuffer(o);
            if ((info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) info.size = 0;
            if (info.size > 0 && track[0] >= 0 && buf != null) {
                buf.position(info.offset); buf.limit(info.offset + info.size);
                muxer.writeSampleData(track[0], buf, info);
            }
            codec.releaseOutputBuffer(o, false);
            if ((info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) return;
        }
    }

    // into the gallery (Movies/imok); before Android 10, the app's own Movies folder
    private String publish(File tmp) throws Exception {
        if (Build.VERSION.SDK_INT < 29) {
            File dir = new File(ctx.getExternalFilesDir(Environment.DIRECTORY_MOVIES), "imok");
            dir.mkdirs();
            File out = new File(dir, tmp.getName());
            copy(new FileInputStream(tmp), new java.io.FileOutputStream(out));
            return out.getAbsolutePath();
        }
        ContentResolver cr = ctx.getContentResolver();
        ContentValues v = new ContentValues();
        v.put(MediaStore.Video.Media.DISPLAY_NAME, tmp.getName());
        v.put(MediaStore.Video.Media.MIME_TYPE, "video/mp4");
        v.put(MediaStore.Video.Media.RELATIVE_PATH, Environment.DIRECTORY_MOVIES + "/imok");
        v.put(MediaStore.Video.Media.IS_PENDING, 1);
        Uri uri = cr.insert(MediaStore.Video.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY), v);
        if (uri == null) throw new Exception("the gallery refused the file");
        OutputStream os = cr.openOutputStream(uri);
        copy(new FileInputStream(tmp), os);
        v.clear(); v.put(MediaStore.Video.Media.IS_PENDING, 0);
        cr.update(uri, v, null, null);
        return "Gallery · Movies/imok";
    }

    private static void copy(InputStream in, OutputStream out) throws Exception {
        byte[] b = new byte[1 << 16]; int n;
        try { while ((n = in.read(b)) > 0) out.write(b, 0, n); }
        finally { in.close(); out.close(); }
    }
}
