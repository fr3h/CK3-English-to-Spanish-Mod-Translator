import sys
from argostranslate import  translate

def main():
    text = sys.stdin.read()
    from_lang = sys.argv[1]
    to_lang = sys.argv[2]
    
    sys.stdout.reconfigure(encoding='utf-8')

    translated_text = translate.translate(text, from_lang, to_lang)

    print(translated_text, end="")

if __name__ == "__main__":
    main()
