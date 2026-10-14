package com.livelayer.app

import org.json.JSONArray

/** One library and one folder, with server/account ownership on each record. */
internal object LocalImageOwnership {
    fun owned(all: JSONArray, scope: String): JSONArray {
        require(scope.isNotBlank())
        val owned = JSONArray()
        for (index in 0 until all.length()) {
            val item = all.getJSONObject(index)
            if (item.optString("ownerScope") == scope) owned.put(item)
        }
        return owned
    }

    fun replaceOwned(all: JSONArray, scope: String, items: JSONArray): JSONArray {
        require(scope.isNotBlank())
        val merged = JSONArray()
        for (index in 0 until all.length()) {
            val item = all.getJSONObject(index)
            if (item.optString("ownerScope") != scope) merged.put(item)
        }
        for (index in 0 until items.length()) {
            merged.put(items.getJSONObject(index).put("ownerScope", scope))
        }
        return merged
    }
}
