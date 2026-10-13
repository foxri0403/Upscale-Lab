const unspecifiedGalleryTag = '장르 설정되지 않음';

// Registration presents Korean labels in 가나다 order, followed by Latin labels.
const selectableGalleryTags = <String>[
  '게임',
  '남자',
  '동물',
  '레트로',
  '밀리터리',
  '비행기',
  '스포츠',
  '애니',
  '여자',
  '자동차',
  '풍경',
  '픽셀 아트',
  'SF',
];

List<String> registrationTags(Set<String> selected) {
  final tags = selectableGalleryTags.where(selected.contains).toList();
  return tags.isEmpty ? [unspecifiedGalleryTag] : tags;
}

const galleryTags = <String>[
  '동물',
  '애니',
  '게임',
  '여자',
  '남자',
  '풍경',
  '픽셀 아트',
  '레트로',
  'SF',
  '스포츠',
  '자동차',
  '비행기',
  '밀리터리',
  unspecifiedGalleryTag,
];
