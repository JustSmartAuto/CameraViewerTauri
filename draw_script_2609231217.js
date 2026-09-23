function process(message) {
    const surfaceNum = 1;
	
    if (message.productWidth === undefined) {
        return
    }

    if (message.surfaceNumber != surfaceNum){
		return;
    }
	
	// 产品名字典
	var ProductName = {
		"1": "1号",
		"2": "2号",
		"3": "3号",
		"4": "4号",
		"5": "5号",
		"6": "6号",
		"7": "7号",
		"8": "8号",
		"9": "9号",
		"10": "10号",
		"11": "11号",
		"12": "12号",
		"13": "13号",
		"14": "30mm标定块",
		"15": "20mm标定块",
		"16": "10mm标定块",
		"17": "5mm标定块",
		
		GetValue: function(key) {
			return this[key] !== undefined ? this[key] : "未知配方";
		}
	};
	
	/**
	 * 截断到指定小数位数（非四舍五入）
	 * @param num 原始数值
	 * @param digits 保留小数位数
	 * @returns 格式化后的字符串
	 */
	function truncateToFixed(num, digits) {
		var factor = Math.pow(10, digits);
		var truncated = Math.trunc(num * factor) / factor;
		return truncated.toFixed(digits);
	}
	
	/**
	 * 计算点到线段的垂足
	 * @param x1 线段起点X
	 * @param y1 线段起点Y
	 * @param x2 线段终点X
	 * @param y2 线段终点Y
	 * @param x0 外部点X
	 * @param y0 外部点Y
	 * @returns {x, y} 垂足坐标（限制在线段范围内）
	 */
	function GetFootOfPerpendicular(x1, y1, x2, y2, x0, y0) {
		var dx = x2 - x1;
		var dy = y2 - y1;

		if (dx === 0 && dy === 0) {
			return { x: x1, y: y1 };
		}

		var t = ((x0 - x1) * dx + (y0 - y1) * dy) / (dx * dx + dy * dy);
		if (t < 0) t = 0;
		if (t > 1) t = 1;

		return {
			x: x1 + t * dx,
			y: y1 + t * dy
		};
	}
	
	/**
	 * 绘制基准线中垂线贯穿线（橙色），以及左右点到中垂线的垂线（品红、青绿）
	 * @param imgW 图像宽度
	 * @param imgH 图像高度
	 * @param baseLineLeftPointX 基准线起点X
	 * @param baseLineLeftPointY 基准线起点Y
	 * @param baseLineRightPointX 基准线终点X
	 * @param baseLineRightPointY 基准线终点Y
	 * @param leftPointX 左点X
	 * @param leftPointY 左点Y
	 * @param rightPointX 右点X
	 * @param rightPointY 右点Y
	 */
	function DrawPerpendicularBisector(imgW, imgH, baseLineLeftPointX, baseLineLeftPointY, baseLineRightPointX, baseLineRightPointY, leftPointX, leftPointY, rightPointX, rightPointY) {
		var midX = (baseLineLeftPointX + baseLineRightPointX) / 2;
		var midY = (baseLineLeftPointY + baseLineRightPointY) / 2;
		var baseDx = baseLineRightPointX - baseLineLeftPointX;
		var baseDy = baseLineRightPointY - baseLineLeftPointY;
		
		// 中垂线方向向量（垂直于基准线）
		var perpVx = baseDy;
		var perpVy = -baseDx;
		var perpLenSq = perpVx * perpVx + perpVy * perpVy;
		
		if (perpLenSq === 0) {
			return;
		}
		
		// --- 绘制中垂线贯穿线（橙色）---
		var tVals = [];
		if (perpVx !== 0) {
			tVals.push(-midX / perpVx);
			tVals.push((imgW - midX) / perpVx);
		}
		if (perpVy !== 0) {
			tVals.push(-midY / perpVy);
			tVals.push((imgH - midY) / perpVy);
		}
		
		tVals.sort(function(a, b) { return a - b; });
		var tMin = tVals[0];
		var tMax = tVals[tVals.length - 1];
		var perpStartX = midX + tMin * perpVx;
		var perpStartY = midY + tMin * perpVy;
		var perpEndX = midX + tMax * perpVx;
		var perpEndY = midY + tMax * perpVy;
		
		PlotLine(imgW, imgH, perpStartX, perpStartY, perpEndX, perpEndY, 8, 0xFFA500);
		
		// --- 绘制左点到中垂线的垂线（品红）---
		var dxL = leftPointX - midX;
		var dyL = leftPointY - midY;
		var tL = (dxL * perpVx + dyL * perpVy) / perpLenSq;
		var footLX = midX + tL * perpVx;
		var footLY = midY + tL * perpVy;
		PlotLine(imgW, imgH, leftPointX, leftPointY, footLX, footLY, 8, 0xFF00FF);
		
		// --- 绘制右点到中垂线的垂线（青绿）---
		var dxR = rightPointX - midX;
		var dyR = rightPointY - midY;
		var tR = (dxR * perpVx + dyR * perpVy) / perpLenSq;
		var footRX = midX + tR * perpVx;
		var footRY = midY + tR * perpVy;
		PlotLine(imgW, imgH, rightPointX, rightPointY, footRX, footRY, 8, 0x00FFFF);
	}
    
    // 使用 test1.jpg 的实际尺寸 9344x7000 作为绘图坐标系
    var imgW = 9344;
    var imgH = 7000;

    // 绘制宽度测量线（红色）
    PlotLine(imgW, imgH,
        message.leftPointX, message.leftPointY,
        message.rightPointX, message.rightPointY,
        8, 0xFF0000)

    // 计算垂足
	var foot = GetFootOfPerpendicular(
		message.baseLineLeftPointX, message.baseLineLeftPointY,
		message.baseLineRightPointX, message.baseLineRightPointY,
		message.topPointX, message.topPointY
	);

	// 绘制高度参考线（蓝色）—— 顶点到垂足
	PlotLine(imgW, imgH,
		message.topPointX, message.topPointY,
		foot.x, foot.y,
		8, 0x0000EE);
	
	// 绘制底座基线参考线（青色）
    PlotLine(imgW, imgH,
        message.baseLineLeftPointX, message.baseLineLeftPointY,
        message.baseLineRightPointX, message.baseLineRightPointY,
        8, 0x00FFDD)

    // 绘制基准线中垂线贯穿线（橙色）及左右点到中垂线的垂线（品红、青绿）
    DrawPerpendicularBisector(imgW, imgH,
        message.baseLineLeftPointX, message.baseLineLeftPointY,
        message.baseLineRightPointX, message.baseLineRightPointY,
        message.leftPointX, message.leftPointY,
        message.rightPointX, message.rightPointY)

    // 绘制测量点（黄色）
    PlotPoint(imgW, imgH, message.leftPointX, message.leftPointY, 30, 0xFFFF00)
    PlotPoint(imgW, imgH, message.rightPointX, message.rightPointY, 30, 0xFFFF00)
    PlotPoint(imgW, imgH, message.topPointX, message.topPointY, 30, 0xFFFF00)

    // 配方号、面号
    PlotString(imgW, imgH, 140, 300,
        "配方号: " + message.productNumber + "(" + ProductName.GetValue(message.productNumber) + ")" + "    面号: " + message.surfaceNumber,
        300, 0x0000FF)

    // 对称度、高度、宽度
    PlotString(imgW, imgH, 140, 600,
        "对称度: " + truncateToFixed(message.symmetry, 4) + "mm",
        300, 0x0000FF)

    PlotString(imgW, imgH, 140, 900,
        "高度: " + truncateToFixed(message.productHeight, 4) + "mm",
        300, 0x0000FF)

    PlotString(imgW, imgH, 140, 1200,
        "宽度: " + truncateToFixed(message.productWidth, 4) + "mm",
        300, 0x0000FF)

    // 时间
    PlotString(imgW, imgH, 140, 1500,
        "时间: " + message.timestamp,
        300, 0x0000FF)

    // 保存渲染图
    var savePath = "D:/CCD图片/整件-尺寸检测/" 
		+ GetTimeStamp("yyyyMMdd") + "/" 
		+ ProductName.GetValue(message.productNumber) + "/面" 
		+ message.surfaceNumber + "/面" 
		+ message.surfaceNumber + "渲染图_" 
		+ message.timestamp + ".jpg";

	SaveRenderedImage(savePath);
}