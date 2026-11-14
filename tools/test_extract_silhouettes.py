"""验证客户端图片头还原，防止后续提取时生成错误轮廓。"""

import io
import unittest

from PIL import Image

from extract_silhouettes import decode_image, excluded_form_ids


class DecodeImageTests(unittest.TestCase):
    def test_标准图片与客户端格式应解码为相同像素(self):
        image = Image.new("RGBA", (300, 300), (30, 60, 90, 128))
        image.putpixel((10, 20), (255, 0, 0, 0))
        stream = io.BytesIO()
        image.save(stream, format="PNG")
        png = stream.getvalue()
        client = b"UF" + png[20:24] + png[:20] + png[24:]
        self.assertEqual(decode_image(png).tobytes(), decode_image(client).tobytes())
        self.assertEqual(image.tobytes(), decode_image(client).tobytes())

    def test_每把刀只保留最初普通形态(self):
        catalog = [
            {"number": str(sid), "name": name}
            for name, ids in [("三日月宗近", [3, 4]), ("日本号", [136, 137]),
                              ("髭切", [107, 108, 109, 110, 111]), ("膝丸", [112, 113, 114, 115]),
                              ("云重", [254])]
            for sid in ids
        ]
        self.assertEqual({4, 137, 108, 109, 110, 111, 113, 114, 115}, excluded_form_ids(catalog))

    def test_异常尺寸应拒绝导入(self):
        stream = io.BytesIO()
        Image.new("RGBA", (30, 30)).save(stream, format="PNG")
        with self.assertRaises(ValueError):
            decode_image(stream.getvalue())


if __name__ == "__main__":
    unittest.main()
