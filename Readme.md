## BMS预览音频文件生成器
---

### **简介**
此工具可以指定(或批量)生成bms预览音频文件并放置到对应的bms文件夹里。
通常用于应付一些bms游戏不支持自动播放预览或bms文件不钦定预览文件([PREVIEW命令](https://github.com/exch-bms2/beatoraja/wiki/%E6%A5%BD%E6%9B%B2%E8%A3%BD%E4%BD%9C%E8%80%85%E5%90%91%E3%81%91%E8%B3%87%E6%96%99#bms%E6%8B%A1%E5%BC%B5%E5%AE%9A%E7%BE%A9))的情况。
<br/>
本项目的BMS解析基于第三方项目[JLChnToZ/Bemusilization](https://github.com/JLChnToZ/Bemusilization)
<br/>
此工具并不会改动bms文件内容。
### **用法**
* 将某个bms文件生成预览音频文件，截取的开始时间为**总长的50%**，结束时间为**总长的75%**,并将生成的文件钦定`preview_neko.ogg`保存到对应的目录.
```
BmsPreviewAudioGenerator.exe -path="E:\[BMS]ENERGY SYNERGY MATRIX" -bms="_hyper.bms" -start="50%" -end="75%" -save_name="preview_neko.ogg"
```

* 批量生成某个某个路径内所有bms文件夹的预览音频，截取的开始时间为**20000ms**，结束时间为**40000ms**,并将生成的文件钦定`preview2857.ogg`保存到对应的目录.(会递归枚举所有含有.bms文件的文件夹,但对于每个文件夹只会生成一份预览音频)
```
BmsPreviewAudioGenerator.exe -batch -path="H:\BOF2012" -start="20000" -end="40000" -save_name="preview2857.ogg"
```
![](https://puu.sh/FgmDX/e95b2c42c8.png)

### **参数**
通用参数格式(推荐):
```
 -your_option1="your_value1" -your_option2="your_value2" -your_option3
```

支持参数:
* `start` : 表示预览音频的起始时间,如果是`xx%`表示相对于bms播放音乐总长的对应位置(比如"25.4%"),如果是具体数值则是以毫秒为单位的绝对位置(比如2857ms => "2857")

* `end` : 表示预览音频的终止时间，参数格式和`start`相同

* `path` : 可以钦定某个bms文件夹，在钦定`batch`批量环境下可以表示含有一堆bms子文件夹的路径(不限制bms子文件夹的对应深度)

* `batch` : 钦定这个表示批量生成,此参数不能和`bms`共存

* `bms` : 钦定bms文件名字,批量环境下会自动选取第一个bms文件,此参数不能和`batch`共存

* `save_name` : 钦定输出的预览音频文件名,貌似也能钦定一个绝对路径

### 注意事项
* 此工具可能占用500m内存(音频解码/混合/编码),梦回2006的电脑就8要用了8.
* 可能有一些奇葩bms文件解析不了
* 给beatoraja预览使用还得rebuild/update database(这货并不会搞没成绩)[原因](https://github.com/exch-bms2/beatoraja/issues/400)